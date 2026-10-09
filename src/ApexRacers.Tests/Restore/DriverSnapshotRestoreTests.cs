using System.Net;
using System.Net.Http.Json;
using ApexRacers.Api.Services;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using ApexRacers.Tests.Lifecycle;
using ApexRacers.Tests.References;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ApexRacers.Tests.Restore;

[Collection(PostgreSqlCollection.Name)]
public sealed class DriverSnapshotRestoreTests(PostgreSqlFixture postgres)
{
    [Theory]
    [InlineData(DriverLifecycleKind.WithdrawPersonal)]
    [InlineData(DriverLifecycleKind.Unlink)]
    [InlineData(DriverLifecycleKind.DeleteUser)]
    public Task Actual_pre_loss_snapshot_requires_current_journal_replay_and_original_time_cleanup(DriverLifecycleKind kind) =>
        LifecycleTopology.RunAsync(postgres, "private-upload-restore-" + kind,
            ["RESTORE-01", "COPY-03", "COPY-04", "COPY-08", "MIGRATE-02"], async test =>
        {
            var ct = test.CancellationToken;
            var clock = new ReferenceClock();
            var scope = SyntheticLifecycleActors.Scope;
            await using (var setup = test.OpenPrimary())
            {
                setup.Cars.Add(new() { Id = 99, Name = "Synthetic restore car", NameAbbreviated = "SRC" });
                setup.Tracks.Add(new() { Id = 42, Name = "Synthetic restore track" });
                await setup.SaveChangesAsync(ct);
                if (kind == DriverLifecycleKind.DeleteUser)
                {
                    var historical = scope with { CustomerId = scope.CustomerId - 1 };
                    var store = new DriverAuthorityStore(setup, clock);
                    var previous = await store.GrantAsync(Proof(historical, clock), Personal, test.Journal, ct);
                    var uploads = new PrivateUploadStore(setup, clock, test.Journal);
                    await uploads.CommitAsync(await uploads.CaptureAsync(previous, ct), Data(clock.GetUtcNow().AddDays(-2), 95), ct);
                    var authority = new DriverAuthorization(store, test.Journal, new UnavailableDriverOwnershipProof(), clock);
                    Assert.True((await authority.TransitionAsync(historical, DriverLifecycleKind.Unlink, Guid.NewGuid(), ct)).Completed);
                }
            }
            await test.GrantAsync();
            PrivateUploadReceipt prepared;
            Guid originalCopy;
            DateTimeOffset acquired;
            Guid week;
            Guid grantId;
            await using (var db = test.OpenPrimary())
            {
                var store = new DriverAuthorityStore(db, clock);
                var owner = (await store.ResolveAsync(scope, DriverConsentScope.Personal, ct))!;
                grantId = owner.GrantId;
                var uploads = new PrivateUploadStore(db, clock, test.Journal);
                await uploads.CommitAsync(await uploads.CaptureAsync(owner, ct), Data(clock.GetUtcNow(), 90), ct);
                week = await SeedOfficialFieldAsync(db, clock, test.Journal, scope.CustomerId, ct);
                var percentile = await uploads.CalculatePercentileAsync(owner, week, 99, ct);
                Assert.Equal(83.33333333333333, percentile!.PercentileRank, 10);
                Assert.Equal(3, percentile.FieldSize);
                await store.CommitCopyAsync(owner, "synthetic restore owner/name copy", test.Journal, ct);
                prepared = await uploads.CaptureAsync(owner, ct);
                var marker = await db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(m => m.Kind == EvidenceCopyKind.PrivateUpload && m.UnavailableAt == null, ct);
                originalCopy = marker.Id; acquired = marker.OriginalAcquiredAt;
            }
            using (var useful = await test.Publisher.Client.GetAsync("/uploaded-bests/before-backup", ct))
            {
                useful.EnsureSuccessStatusCode();
                Assert.Equal(90, Assert.Single((await useful.Content.ReadFromJsonAsync<PrivateUploadedBest[]>(ct))!).BestLapSeconds);
                await test.WaitAsync("before-backup", "checkpointed");
            }
            var snapshot = await PrimarySnapshot.CaptureAsync(postgres, test.PrimaryConnection, ct);
            var passed = false;
            DriverLifecycleOutcome? loss = null;
            try
            {
                loss = await test.TransitionAsync(Guid.NewGuid(), kind);
                Assert.True(loss.Completed);
                await snapshot.RestoreAsync(postgres, test.PrimaryConnection, ct);
                await test.RestartBothAsync();
                await DeniedAsync(test, "before-reconcile");
                await test.FaultPublisherAsync("journal-read");
                await DeniedAsync(test, "unknown-current-enforcement");
                await test.ClearPublisherFaultAsync("journal-read");
                await using (var restored = test.OpenPrimary())
                {
                    Assert.True((await restored.DriverAuthorizationGrants.SingleAsync(g => g.Id == grantId, ct)).PersonalConsentVersion is not null);
                    Assert.NotEmpty(await restored.CarPercentileResults.IgnoreQueryFilters().ToListAsync(ct));
                    Assert.NotEmpty(await restored.DriverTrackedCopies.ToListAsync(ct));
                    Assert.Equal(acquired, (await restored.EvidenceCopyMarkers.SingleAsync(m => m.Id == originalCopy, ct)).OriginalAcquiredAt);
                    var authority = new DriverAuthorization(new(restored, clock), test.Journal, new UnavailableDriverOwnershipProof(), clock);
                    Assert.True((await authority.RecoverAsync(new(loss.OperationId, grantId, scope, kind, loss.OriginalLossAt), ct)).Completed);
                    await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => new PrivateUploadStore(restored, clock, test.Journal)
                        .CommitAsync(prepared, Data(clock.GetUtcNow().AddSeconds(1), 80), ct));
                }
                await DeniedAsync(test, "after-reconcile-closed");
                if (kind != DriverLifecycleKind.DeleteUser)
                {
                    clock.Set(loss.OriginalLossAt.AddDays(1));
                    await using var recovered = test.OpenPrimary();
                    var fresh = await new DriverAuthorityStore(recovered, clock).GrantAsync(Proof(scope, clock), Personal, test.Journal, ct);
                    Assert.Equal(90, Assert.Single(await new PrivateUploadStore(recovered, clock, test.Journal).ReadBestsAsync(fresh, ct)).BestLapSeconds);
                    var active = await recovered.EvidenceCopyMarkers.SingleAsync(m => m.Kind == EvidenceCopyKind.PrivateUpload && m.UnavailableAt == null, ct);
                    Assert.Equal(acquired, active.OriginalAcquiredAt);
                    using var useful = await test.Publisher.Client.GetAsync("/uploaded-bests/recovered-original", ct);
                    useful.EnsureSuccessStatusCode();
                    Assert.Equal(90, Assert.Single((await useful.Content.ReadFromJsonAsync<PrivateUploadedBest[]>(ct))!).BestLapSeconds);
                    await test.WaitAsync("recovered-original", "checkpointed");
                    // Restore the same pre-loss bytes again: recovery is never inferred from the backup.
                    await snapshot.RestoreAsync(postgres, test.PrimaryConnection, ct);
                    await test.RestartBothAsync();
                    await DeniedAsync(test, "second-restore");
                }
                clock.Set(loss.OriginalLossAt.AddDays(kind == DriverLifecycleKind.DeleteUser ? 8 : 98));
                await using (var overdue = test.OpenPrimary())
                {
                    var store = new DriverAuthorityStore(overdue, clock);
                    var authority = new DriverAuthorization(store, test.Journal, new UnavailableDriverOwnershipProof(), clock);
                    Assert.True((await authority.RecoverAsync(new(loss.OperationId, grantId, scope, kind, loss.OriginalLossAt), ct)).Completed);
                    await store.RemoveDueCopiesAsync(ct);
                    await new EvidenceCopyLifecycle(overdue, clock, test.Journal).ReconcileAsync(ct);
                    Assert.Empty(await overdue.PrivateUploadSessions.ToListAsync(ct));
                    Assert.Empty(await overdue.PrivateUploadedLaps.ToListAsync(ct));
                    Assert.Empty(await overdue.CarPercentileResults.IgnoreQueryFilters().ToListAsync(ct));
                    Assert.Empty(await overdue.DriverTrackedCopies.ToListAsync(ct));
                    var marker = await overdue.EvidenceCopyMarkers.SingleAsync(m => m.Id == originalCopy, ct);
                    Assert.Equal(acquired, marker.OriginalAcquiredAt);
                    Assert.Equal(loss.OriginalLossAt, marker.UnavailableAt);
                    Assert.Equal(loss.OriginalLossAt.AddDays(kind == DriverLifecycleKind.DeleteUser ? 7 : 97), marker.RemovalDueAt);
                    Assert.NotNull(marker.VerifiedRemovedAt);
                    Assert.False((await new EvidenceCopyLifecycle(overdue, clock, test.Journal).InspectAsync(ct)).BackupExpiryVerified);
                    Assert.Equal(new long[] { scope.CustomerId, 22222, 33333 }.Order(), await overdue.SubsessionResults.OrderBy(r => r.CustId).Select(r => r.CustId).ToArrayAsync(ct));
                    Assert.True(await overdue.Subsessions.AnyAsync(s => s.OfficialSession && s.WeekId == week, ct));
                    if (kind == DriverLifecycleKind.DeleteUser)
                    {
                        Assert.False(await overdue.Users.AnyAsync(u => u.Id == scope.UserId, ct));
                        Assert.NotEmpty(await overdue.DriverLifecycleOperations.Where(o => o.Kind == kind).ToListAsync(ct));
                        // A returned account row is no escape from the independent User-wide veto.
                        overdue.Users.Add(new() { Id = scope.UserId, DisplayName = "Synthetic returned account row", EmailConfirmed = true });
                        await overdue.SaveChangesAsync(ct);
                        foreach (var customer in new[] { scope.CustomerId - 1, scope.CustomerId, scope.CustomerId + 1 })
                            await Assert.ThrowsAsync<InvalidOperationException>(() => store.GrantFreshCollectionAsync(Proof(scope with { CustomerId = customer }, clock), Personal, test.Journal, ct));
                        await new EvidenceCopyLifecycle(overdue, clock, test.Journal).ReconcileAsync(ct);
                        Assert.False(await overdue.Users.AnyAsync(u => u.Id == scope.UserId, ct));
                    }
                    else
                        await Assert.ThrowsAsync<InvalidOperationException>(() => store.GrantAsync(Proof(scope, clock), Personal, test.Journal, ct));
                }
                await DeniedAsync(test, "after-overdue-removal");
                passed = true;
            }
            finally
            {
                await snapshot.RecordAsync("pre-loss-" + kind, new
                {
                    Kind = kind,
                    Loss = loss,
                    OriginalAcquiredAt = acquired,
                    OriginalCopy = originalCopy,
                    Topology = "Two actual Kestrel processes, primary PostgreSQL, separate persisted current enforcement journal",
                    UsefulRecovery = kind != DriverLifecycleKind.DeleteUser
                }, passed);
            }
        });

    private static DriverConsent Personal => new(DriverAuthorizationPolicy.PersonalConsentVersion);
    private static VerifiedDriverProof Proof(DriverScope scope, ReferenceClock clock) => new(Guid.NewGuid(), scope, clock.GetUtcNow(), "controlled377-synthetic-proof", "Synthetic Restore Driver");
    private static PrivateUploadData Data(DateTimeOffset at, double seconds) => new(99, 42, at, LapSessionType.Unknown, [new PrivateLap(1, seconds)]);
    private static async Task DeniedAsync(LifecycleTopology test, string id)
    {
        using var response = await test.Publisher.Client.GetAsync("/uploaded-bests/" + id, test.CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var body = await response.Content.ReadAsStringAsync(test.CancellationToken);
        Assert.DoesNotContain("bestLapSeconds", body); Assert.DoesNotContain("Synthetic Restore Driver", body);
    }

    private static async Task<Guid> SeedOfficialFieldAsync(AppDbContext db, ReferenceClock clock, IDriverEnforcementJournal journal, int customer, CancellationToken ct)
    {
        db.Series.Add(new() { Id = 1, Name = "Synthetic Restore Series" });
        db.Seasons.Add(new() { Id = 1, SeriesId = 1, Active = true });
        db.CarClasses.Add(new() { Id = 1, Name = "Synthetic Restore Class", ShortName = "SRC" });
        var week = Guid.NewGuid();
        db.Weeks.Add(new() { Id = week, SeasonId = 1, TrackId = 42, StartDate = new(2026, 10, 6) });
        await db.SaveChangesAsync(ct);
        var copies = new EvidenceCopyLifecycle(db, clock, journal);
        var purpose = await copies.OpenSyntheticPurposeAsync(EvidencePurposeKind.IndependentOfficial, 1, ct: ct);
        var receipt = await copies.CaptureAsync(purpose, EvidenceCopyKind.OfficialField, "synthetic-restore-field-v1", ct: ct);
        await copies.CommitAsync(receipt, new OfficialFieldBatch(new() { Id = 1, SeasonId = 1, WeekId = week, TrackId = 42, OfficialSession = true, StartTime = clock.GetUtcNow() },
            [new() { SubsessionId = 1, CustId = customer, CarId = 99, CarClassId = 1, BestLapSeconds = 105 },
             new() { SubsessionId = 1, CustId = 22222, CarId = 99, CarClassId = 1, BestLapSeconds = 100 },
             new() { SubsessionId = 1, CustId = 33333, CarId = 99, CarClassId = 1, BestLapSeconds = 110 }]), ct);
        return week;
    }
}
