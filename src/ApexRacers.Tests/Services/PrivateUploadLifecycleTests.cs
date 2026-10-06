using ApexRacers.Api.Services;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace ApexRacers.Tests.Services;

[Collection(PostgreSqlCollection.Name)]
public sealed class PrivateUploadLifecycleTests(PostgreSqlFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DriverConsent Personal = new(DriverAuthorizationPolicy.PersonalConsentVersion);

    [Fact]
    public async Task MatchingProofAndPersonalOptInPersistTypedLapsAndUsefulAllTimeBestIdempotently()
    {
        await using var f = await CreateAsync();
        var access = await f.GrantAsync();
        var first = await f.UploadAsync(90);
        Assert.True(first.Persisted);
        await f.UploadAsync(90);
        Assert.Single(await f.Db.PrivateUploadSessions.ToListAsync(Ct));
        Assert.Equal(2, await f.Db.PrivateUploadedLaps.CountAsync(Ct));
        var best = Assert.Single(await f.Uploads.ReadBestsAsync(access, Ct));
        Assert.Equal(90, best.BestLapSeconds);
        Assert.Equal(2, best.LapCount);
        Assert.Equal("Catalog Car", best.CarName);
        Assert.Equal("Catalog Track", best.TrackName);
        var stored = await f.Db.PrivateUploadSessions.SingleAsync(Ct);
        Assert.Equal(f.Scope.UserId, stored.UserId);
        Assert.Equal(f.Scope.CustomerId, stored.CustomerId);
        var marker = await f.Db.EvidenceCopyMarkers.SingleAsync(c => c.Id == stored.EvidenceCopyId, Ct);
        Assert.Equal(EvidenceCopyKind.PrivateUpload, marker.Kind);
        Assert.Empty(await f.Db.UploadedLaps.IgnoreQueryFilters().ToListAsync(Ct));
        Assert.DoesNotContain(f.Db.Model.FindEntityType(typeof(PrivateUploadSession))!.GetProperties(),
            p => p.Name.Contains("Name") || p.Name.Contains("Payload") || p.Name.Contains("Raw"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(999999)]
    public async Task MissingOrMismatchedRecorderCannotPersistAndFailureIsGeneric(int recorder)
    {
        await using var f = await CreateAsync();
        await f.GrantAsync();
        var stream = FakeIbtBuilder.Build(customerId: recorder);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ProcessSyntheticAsync(stream, f.Scope, Ct));
        Assert.Equal("This telemetry cannot be attributed to this account.", error.Message);
        Assert.Empty(await f.Db.PrivateUploadSessions.ToListAsync(Ct));
        Assert.Empty(await f.Db.PrivateUploadedLaps.ToListAsync(Ct));
        Assert.False(stream.CanRead);
    }

    [Fact]
    public async Task StoredClaimOtherUserSharingAndForgedRealScopeSupplyNoPersonalAuthority()
    {
        await using var f = await CreateAsync();
        var stream = FakeIbtBuilder.Build(customerId: f.Scope.CustomerId);
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => f.Service.ProcessSyntheticAsync(stream, f.Scope, Ct));
        Assert.False(stream.CanRead);
        var access = await f.GrantAsync();
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => f.Uploads.CaptureAsync(access with { Purpose = DriverConsentScope.Sharing }, Ct));
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => f.Uploads.CaptureAsync(access with { Scope = f.Scope with { UserId = Guid.NewGuid() } }, Ct));
        var real = FakeIbtBuilder.Build(customerId: f.Scope.CustomerId);
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => f.Service.ProcessSyntheticAsync(real, f.Scope with { Provenance = DataProvenance.Real }, Ct));
        Assert.Empty(await f.Db.PrivateUploadSessions.ToListAsync(Ct));
        Assert.False(real.CanRead);
    }

    [Theory]
    [InlineData(DriverLifecycleKind.WithdrawPersonal)]
    [InlineData(DriverLifecycleKind.Unlink)]
    [InlineData(DriverLifecycleKind.RevokeProof)]
    public async Task LossImmediatelyHidesPhysicalPersonalCopiesAndRejectsDelayedWriter(DriverLifecycleKind kind)
    {
        await using var f = await CreateAsync();
        var owner = await f.GrantAsync();
        await f.UploadAsync(90);
        var delayed = await f.Uploads.CaptureAsync(owner, Ct);
        var outcome = await f.Authority.TransitionAsync(f.Scope, kind, Guid.NewGuid(), Ct);
        Assert.True(outcome.Completed);
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => f.Uploads.CommitAsync(delayed, f.Data(85, 1), Ct));
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => f.Uploads.ReadBestsAsync(owner, Ct));
        Assert.Single(await f.Db.PrivateUploadSessions.ToListAsync(Ct));
        var marker = await f.Db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(c => c.Kind == EvidenceCopyKind.PrivateUpload, Ct);
        Assert.Equal(outcome.OriginalLossAt, marker.UnavailableAt);
        Assert.Equal(outcome.OriginalLossAt.AddDays(97), marker.RemovalDueAt);
        await new EvidenceCopyLifecycle(f.Db, f.Clock).ReconcileAsync(Ct);
        Assert.Single(await f.Db.PrivateUploadSessions.ToListAsync(Ct));
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public async Task FreshProofRestoresOnlyBeforeDayNinetyWithoutRenewingOriginalMarkerClocks(int seconds, bool restores)
    {
        await using var f = await CreateAsync();
        await f.GrantAsync();
        await f.UploadAsync(90);
        var original = await f.Db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(c => c.Kind == EvidenceCopyKind.PrivateUpload, Ct);
        var loss = await f.Authority.TransitionAsync(f.Scope, DriverLifecycleKind.Unlink, Guid.NewGuid(), Ct);
        f.Clock.Now = loss.OriginalLossAt.AddDays(90).AddSeconds(seconds);
        f.Proof.Receipt = f.Proof.Receipt! with { ReceiptId = Guid.NewGuid(), VerifiedAt = f.Clock.Now };
        if (restores)
        {
            var owner = await f.GrantAsync();
            Assert.Equal(90, Assert.Single(await f.Uploads.ReadBestsAsync(owner, Ct)).BestLapSeconds);
            var active = await f.Db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(c => c.Kind == EvidenceCopyKind.PrivateUpload && c.UnavailableAt == null, Ct);
            Assert.Equal(original.OriginalAcquiredAt, active.OriginalAcquiredAt);
            await new EvidenceCopyLifecycle(f.Db, f.Clock).ReconcileAsync(Ct);
            Assert.Single(await f.Db.PrivateUploadSessions.ToListAsync(Ct));
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => f.GrantAsync());
            await new EvidenceCopyLifecycle(f.Db, f.Clock).ReconcileAsync(Ct);
            Assert.Empty(await f.Db.PrivateUploadSessions.ToListAsync(Ct));
            Assert.Empty(await f.Db.PrivateUploadedLaps.ToListAsync(Ct));
        }
        var history = await f.Db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(c => c.Id == original.Id, Ct);
        Assert.Equal(original.OriginalAcquiredAt, history.OriginalAcquiredAt);
        Assert.Equal(loss.OriginalLossAt, history.UnavailableAt);
        Assert.Equal(loss.OriginalLossAt.AddDays(97), history.RemovalDueAt);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task PhysicalRemovalAtDayNinetySevenAndRetriesPreserveDeadline(int seconds)
    {
        await using var f = await CreateAsync();
        await f.GrantAsync();
        await f.UploadAsync(90);
        var loss = await f.Authority.TransitionAsync(f.Scope, DriverLifecycleKind.WithdrawPersonal, Guid.NewGuid(), Ct);
        f.Clock.Now = loss.OriginalLossAt.AddDays(97).AddSeconds(seconds);
        await using var restarted = f.Restart();
        var cleanup = new EvidenceCopyLifecycle(restarted, f.Clock);
        await cleanup.ReconcileAsync(Ct);
        await cleanup.ReconcileAsync(Ct);
        Assert.Empty(await restarted.PrivateUploadSessions.ToListAsync(Ct));
        Assert.Empty(await restarted.PrivateUploadedLaps.ToListAsync(Ct));
        var original = await restarted.EvidenceCopyMarkers.SingleAsync(c => c.Kind == EvidenceCopyKind.PrivateUpload, Ct);
        Assert.Equal(loss.OriginalLossAt.AddDays(97), original.RemovalDueAt);
        Assert.NotNull(original.VerifiedRemovedAt);
        Assert.False((await cleanup.InspectAsync(Ct)).BackupExpiryVerified);
    }

    [Fact]
    public async Task AnotherUserOrAnotherDriverCannotRecoverOriginalDormantSources()
    {
        await using var f = await CreateAsync();
        await f.GrantAsync();
        await f.UploadAsync(90);
        await f.Authority.TransitionAsync(f.Scope, DriverLifecycleKind.Unlink, Guid.NewGuid(), Ct);
        f.Clock.Now = f.Clock.Now.AddDays(1);
        var next = f.Scope with { CustomerId = f.Scope.CustomerId + 1 };
        f.Proof.Receipt = f.Proof.Receipt! with { ReceiptId = Guid.NewGuid(), Scope = next, VerifiedAt = f.Clock.Now };
        var nextOwner = await f.Authority.GrantAsync(next, Personal, Ct);
        Assert.Empty(await f.Uploads.ReadBestsAsync(nextOwner, Ct));
        var other = f.Scope with { UserId = Guid.NewGuid() };
        f.Proof.Receipt = f.Proof.Receipt with { ReceiptId = Guid.NewGuid(), Scope = other };
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Authority.GrantAsync(other, Personal, Ct));
        Assert.Single(await f.Db.PrivateUploadSessions.Where(s => s.UserId == f.Scope.UserId && s.CustomerId == f.Scope.CustomerId).ToListAsync(Ct));
    }

    [Fact]
    public async Task UserWideDeletionCoversHistoryTightensDormancyAndSeparatelyReportsBackupExpiry()
    {
        await using var f = await CreateAsync();
        await f.GrantAsync();
        await f.UploadAsync(90);
        var first = await f.Authority.TransitionAsync(f.Scope, DriverLifecycleKind.Unlink, Guid.NewGuid(), Ct);
        f.Clock.Now = first.OriginalLossAt.AddDays(96);
        var next = f.Scope with { CustomerId = f.Scope.CustomerId + 1 };
        f.Proof.Receipt = f.Proof.Receipt! with { ReceiptId = Guid.NewGuid(), Scope = next, VerifiedAt = f.Clock.Now };
        var nextOwner = await f.Authority.GrantAsync(next, Personal, Ct);
        var nextReceipt = await f.Uploads.CaptureAsync(nextOwner, Ct);
        await f.Uploads.CommitAsync(nextReceipt, f.Data(80, 1), Ct);
        var operationId = Guid.NewGuid();
        var deletion = await f.Authority.TransitionAsync(next, DriverLifecycleKind.DeleteUser, operationId, Ct);
        var pending = await f.Uploads.InspectUserAsync(f.Scope.UserId, Ct);
        Assert.True(pending.WithdrawalCompleted);
        Assert.False(pending.LiveErasureVerified);
        Assert.Equal(first.OriginalLossAt.AddDays(97), pending.LiveRemovalDueAt);
        Assert.Equal(deletion.OriginalLossAt.AddDays(14), pending.BackupExpiryDueAt);
        f.Clock.Now = deletion.OriginalLossAt.AddDays(7);
        await using var restarted = f.Restart();
        var restartedStore = new DriverAuthorityStore(restarted, f.Clock);
        var restartedAuthority = new DriverAuthorization(restartedStore, f.Journal, f.Proof, f.Clock);
        Assert.True((await restartedAuthority.TransitionAsync(next, DriverLifecycleKind.DeleteUser, operationId, Ct)).Completed);
        await new EvidenceCopyLifecycle(restarted, f.Clock).ReconcileAsync(Ct);
        Assert.Empty(await restarted.PrivateUploadSessions.ToListAsync(Ct));
        Assert.Empty(await restarted.PrivateUploadedLaps.ToListAsync(Ct));
        Assert.False(await restarted.Users.AnyAsync(u => u.Id == f.Scope.UserId, Ct));
        var complete = await new PrivateUploadStore(restarted, f.Clock, f.Journal).InspectUserAsync(f.Scope.UserId, Ct);
        Assert.True(complete.LiveErasureVerified);
        Assert.False(complete.BackupExpiryVerified);
        var alternate = next with { CustomerId = next.CustomerId + 1 };
        f.Proof.Receipt = f.Proof.Receipt with { ReceiptId = Guid.NewGuid(), Scope = alternate, VerifiedAt = f.Clock.Now };
        await Assert.ThrowsAsync<InvalidOperationException>(() => restartedAuthority.GrantAsync(alternate, Personal, Ct));
    }

    [Fact]
    public async Task PostgreSqlRejectsUnmarkedWritesReusedMarkersAndMutatingTheOriginalOwner()
    {
        await using var f = await CreateAsync();
        await f.GrantAsync();
        await f.UploadAsync(90);
        var copy = await f.Db.PrivateUploadSessions.AsNoTracking().SingleAsync(Ct);
        await using var raw = f.Restart();
        await Assert.ThrowsAsync<PostgresException>(() => raw.Users.Where(u => u.Id == f.Scope.UserId).ExecuteDeleteAsync(Ct));
        await Assert.ThrowsAsync<PostgresException>(() => raw.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE iracing.\"PrivateUploadSessions\" SET \"UserId\" = {Guid.NewGuid()} WHERE \"Id\" = {copy.Id}", Ct));
        await Assert.ThrowsAsync<PostgresException>(() => raw.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE iracing.\"PrivateUploadSessions\" SET \"EvidenceCopyId\" = {copy.EvidenceCopyId} WHERE \"Id\" = {copy.Id}", Ct));
        await Assert.ThrowsAsync<PostgresException>(() => raw.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE iracing.\"PrivateUploadedLaps\" SET \"LapTimeSeconds\" = 70 WHERE \"SessionId\" = {copy.Id}", Ct));
        Assert.Equal(90, Assert.Single(await f.Uploads.ReadBestsAsync((await f.Authority.ResolveAsync(f.Scope, DriverConsentScope.Personal, Ct))!, Ct)).BestLapSeconds);
    }

    private async Task<Fixture> CreateAsync()
    {
        var options = await postgres.CreateOptionsAsync(Ct);
        await using (var setup = new AppDbContext(options))
        {
            await setup.Database.EnsureDeletedAsync(Ct);
            await setup.Database.MigrateAsync(Ct);
        }
        var f = new Fixture(options);
        f.Db.Users.Add(new() { Id = f.Scope.UserId, DisplayName = "Synthetic User", IRacingCustomerId = 999999 });
        f.Db.Cars.Add(new() { Id = 99, Name = "Catalog Car", NameAbbreviated = "CC" });
        f.Db.Tracks.Add(new() { Id = 42, Name = "Catalog Track", ConfigName = "Catalog Layout" });
        await f.Db.SaveChangesAsync(Ct);
        return f;
    }

    [Fact]
    public async Task UsefulOwnerPercentileKeepsExactArithmeticAndTracksBothPrivateAndIndependentSources()
    {
        await using var f = await CreateAsync();
        var owner = await f.GrantAsync();
        await f.UploadAsync(90);
        var weekId = await FieldAsync(f);
        var first = await f.Uploads.CalculatePercentileAsync(owner, weekId, 99, Ct);
        Assert.NotNull(first);
        Assert.Equal(90, first.LapSeconds);
        Assert.Equal(LapEvidence.UploadedLap, first.Evidence);
        Assert.Equal(83.33333333333333, first.PercentileRank, 10);
        Assert.Equal(1, first.FieldPosition);
        Assert.Equal(34, first.TopSharePercent);
        Assert.Equal(3, first.FieldSize);
        Assert.Equal(first, await f.Uploads.CalculatePercentileAsync(owner, weekId, 99, Ct));
        var derived = await f.Db.CarPercentileResults.SingleAsync(Ct);
        Assert.Equal(2, await f.Db.EvidenceCopyDependencies.CountAsync(d => d.CopyId == derived.EvidenceCopyId, Ct));
        using var outside = FakeIbtBuilder.Build(laps: 1, lapTime: 80, customerId: f.Scope.CustomerId,
            sessionDate: f.Clock.Now.AddDays(-10).ToUnixTimeSeconds());
        await f.Service.ProcessSyntheticAsync(outside, f.Scope, Ct);
        Assert.Equal(80, Assert.Single(await f.Uploads.ReadBestsAsync(owner, Ct)).BestLapSeconds);
        Assert.Equal(first, await f.Uploads.CalculatePercentileAsync(owner, weekId, 99, Ct));
        await f.Authority.TransitionAsync(f.Scope, DriverLifecycleKind.WithdrawPersonal, Guid.NewGuid(), Ct);
        await new EvidenceCopyLifecycle(f.Db, f.Clock).ReconcileAsync(Ct);
        Assert.Empty(await f.Db.CarPercentileResults.IgnoreQueryFilters().ToListAsync(Ct));
        Assert.Equal(3, await f.Db.SubsessionResults.CountAsync(Ct));
        Assert.Equal(2, await f.Db.PrivateUploadSessions.CountAsync(Ct));
    }

    [Fact]
    public async Task NewContributionInvalidatesOwnerSummaryAndPreparedWriterWithoutEndingEarlierSources()
    {
        await using var f = await CreateAsync();
        var owner = await f.GrantAsync();
        await f.UploadAsync(90);
        var weekId = await FieldAsync(f);
        await f.Uploads.CalculatePercentileAsync(owner, weekId, 99, Ct);
        var oldSummary = await f.Db.CarPercentileResults.AsNoTracking().SingleAsync(Ct);
        var original = await f.Db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(c => c.Kind == EvidenceCopyKind.PrivateUpload, Ct);
        var delayed = await f.Uploads.CaptureAsync(owner, Ct);
        f.Clock.Now = f.Clock.Now.AddSeconds(1);
        await f.UploadAsync(85);
        Assert.Empty(await f.Db.CarPercentileResults.ToListAsync(Ct));
        var summaryMarker = await f.Db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(c => c.Id == oldSummary.EvidenceCopyId, Ct);
        Assert.Equal(f.Clock.Now, summaryMarker.UnavailableAt);
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => f.Uploads.CommitAsync(delayed, f.Data(80, 1), Ct));
        var source = await f.Db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(c => c.Id == original.Id, Ct);
        Assert.Equal(original.OriginalAcquiredAt, source.OriginalAcquiredAt);
        Assert.Null(source.UnavailableAt);
        var current = await f.Uploads.CalculatePercentileAsync(owner, weekId, 99, Ct);
        Assert.Equal(85, current!.LapSeconds);
        Assert.Equal(83.33333333333333, current.PercentileRank, 10);
        Assert.Single(await f.Db.CarPercentileResults.IgnoreQueryFilters().ToListAsync(Ct));
        Assert.Equal(2, await f.Db.PrivateUploadSessions.CountAsync(Ct));
        Assert.Equal(3, await f.Db.SubsessionResults.CountAsync(Ct));
    }

    [Fact]
    public async Task PhysicalSourceLossInvalidatesPreparedPublicationAndDerivativeWhileIndependentFieldSurvives()
    {
        await using var f = await CreateAsync();
        var owner = await f.GrantAsync();
        await f.UploadAsync(90);
        await f.Uploads.CalculatePercentileAsync(owner, await FieldAsync(f), 99, Ct);
        var snapshot = await f.Uploads.ReadSnapshotAsync(owner, Ct);
        var session = await f.Db.PrivateUploadSessions.AsNoTracking().SingleAsync(Ct);
        await using (var raw = f.Restart())
            await raw.PrivateUploadedLaps.Where(l => l.SessionId == session.Id).ExecuteDeleteAsync(Ct);
        Assert.False(await f.Uploads.SnapshotCurrentAsync(owner, snapshot.Sources, Ct));
        Assert.Empty(await f.Db.CarPercentileResults.ToListAsync(Ct));
        await new EvidenceCopyLifecycle(f.Db, f.Clock).ReconcileAsync(Ct);
        Assert.Empty(await f.Db.CarPercentileResults.IgnoreQueryFilters().ToListAsync(Ct));
        Assert.Equal(3, await f.Db.SubsessionResults.CountAsync(Ct));
    }

    [Theory]
    [InlineData("journal-intent-recorded")]
    [InlineData("primary-closed")]
    public async Task InterruptedUserDeletionVetoesEveryCustomerIdAndRecoversOriginalOperation(string boundary)
    {
        await using var f = await CreateAsync();
        await f.GrantAsync();
        await f.UploadAsync(90);
        var id = Guid.NewGuid();
        var fault = new DriverAuthorization(f.Store, f.Journal, f.Proof, f.Clock, new Fault(boundary));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fault.TransitionAsync(f.Scope, DriverLifecycleKind.DeleteUser, id, Ct));
        var newScope = f.Scope with { CustomerId = f.Scope.CustomerId + 1 };
        f.Proof.Receipt = f.Proof.Receipt! with { Scope = newScope, ReceiptId = Guid.NewGuid() };
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Authority.GrantAsync(newScope, Personal, Ct));
        var original = f.Journal.Intents[id];
        f.Clock.Now = f.Clock.Now.AddDays(2);
        await using var restarted = f.Restart();
        var authority = new DriverAuthorization(new(restarted, f.Clock), f.Journal, f.Proof, f.Clock);
        var recovered = await authority.RecoverAsync(original, Ct);
        Assert.True(recovered.Completed);
        Assert.Equal(original.OriginalLossAt, recovered.OriginalLossAt);
        var status = await new PrivateUploadStore(restarted, f.Clock, f.Journal).InspectUserAsync(f.Scope.UserId, Ct);
        Assert.Equal(original.OriginalLossAt.AddDays(7), status.LiveRemovalDueAt);
        Assert.Equal(original.OriginalLossAt.AddDays(14), status.BackupExpiryDueAt);
    }

    [Fact]
    public async Task UserDeletionWaitsForWritersOnBothOldAndCurrentAssociations()
    {
        await using var f = await CreateAsync();
        var old = await f.GrantAsync();
        var incarnation = Guid.NewGuid();
        var oldWriter = (await f.Store.AdmitAsync(old, incarnation, f.Journal, Ct))!;
        var unlink = await f.Authority.TransitionAsync(f.Scope, DriverLifecycleKind.Unlink, Guid.NewGuid(), Ct);
        Assert.False(unlink.Completed);
        var nextScope = f.Scope with { CustomerId = f.Scope.CustomerId + 1 };
        f.Proof.Receipt = f.Proof.Receipt! with { Scope = nextScope, ReceiptId = Guid.NewGuid() };
        var next = await f.Authority.GrantAsync(nextScope, Personal, Ct);
        var nextWriter = (await f.Store.AdmitAsync(next, incarnation, f.Journal, Ct))!;
        var id = Guid.NewGuid();
        Assert.Equal(2, (await f.Authority.TransitionAsync(nextScope, DriverLifecycleKind.DeleteUser, id, Ct)).PendingWriters);
        await f.Store.CheckpointAsync(oldWriter.Id, incarnation, Ct);
        Assert.Equal(1, (await f.Authority.TransitionAsync(nextScope, DriverLifecycleKind.DeleteUser, id, Ct)).PendingWriters);
        await f.Store.CheckpointAsync(nextWriter.Id, incarnation, Ct);
        Assert.True((await f.Authority.TransitionAsync(nextScope, DriverLifecycleKind.DeleteUser, id, Ct)).Completed);
        Assert.True((await f.Uploads.InspectUserAsync(f.Scope.UserId, Ct)).WithdrawalCompleted);
    }

    [Theory]
    [InlineData(7, -1)]
    [InlineData(7, 0)]
    [InlineData(7, 1)]
    [InlineData(14, -1)]
    [InlineData(14, 0)]
    [InlineData(14, 1)]
    public async Task DeletionBoundariesNeverExtendLiveDeadlineOrClaimBackupExpiry(int days, int seconds)
    {
        await using var f = await CreateAsync();
        await f.GrantAsync();
        await f.UploadAsync(90);
        var deletion = await f.Authority.TransitionAsync(f.Scope, DriverLifecycleKind.DeleteUser, Guid.NewGuid(), Ct);
        f.Clock.Now = deletion.OriginalLossAt.AddDays(days).AddSeconds(seconds);
        var before = await f.Uploads.InspectUserAsync(f.Scope.UserId, Ct);
        Assert.Equal(days > 7 || seconds > 0, before.OverdueCopies > 0);
        Assert.Equal(deletion.OriginalLossAt.AddDays(7), before.LiveRemovalDueAt);
        Assert.Equal(deletion.OriginalLossAt.AddDays(14), before.BackupExpiryDueAt);
        await new EvidenceCopyLifecycle(f.Db, f.Clock).ReconcileAsync(Ct);
        var after = await f.Uploads.InspectUserAsync(f.Scope.UserId, Ct);
        Assert.True(after.LiveErasureVerified);
        Assert.Equal(0, after.OverdueCopies);
        Assert.False(after.BackupExpiryVerified);
        Assert.Equal(before.LiveRemovalDueAt, after.LiveRemovalDueAt);
        Assert.Equal(before.BackupExpiryDueAt, after.BackupExpiryDueAt);
        f.Proof.Receipt = f.Proof.Receipt! with { ReceiptId = Guid.NewGuid(), VerifiedAt = f.Clock.Now };
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Authority.GrantFreshCollectionAsync(f.Scope, Personal, Ct));
    }

    [Fact]
    public async Task CleanupStatusCountsOverdueOpaquePayloadsSeparatelyFromTypedErasure()
    {
        await using var f = await CreateAsync();
        var owner = await f.GrantAsync();
        await f.UploadAsync(90);
        await f.Store.CommitCopyAsync(owner, "synthetic opaque payload", f.Journal, Ct);
        var loss = await f.Authority.TransitionAsync(f.Scope, DriverLifecycleKind.WithdrawPersonal, Guid.NewGuid(), Ct);
        f.Clock.Now = loss.OriginalLossAt.AddDays(1).AddSeconds(1);
        var pending = await f.Uploads.InspectUserAsync(f.Scope.UserId, Ct);
        Assert.Equal(2, pending.RetainedCopies);
        Assert.Equal(1, pending.OverdueCopies);
        Assert.Equal(loss.OriginalLossAt.AddDays(1), pending.LiveRemovalDueAt);
        Assert.False(pending.LiveErasureVerified);
        await f.Store.RemoveDueCopiesAsync(Ct);
        var remaining = await f.Uploads.InspectUserAsync(f.Scope.UserId, Ct);
        Assert.Equal(1, remaining.RetainedCopies);
        Assert.Equal(0, remaining.OverdueCopies);
        Assert.Equal(loss.OriginalLossAt.AddDays(97), remaining.LiveRemovalDueAt);
    }

    private static async Task<Guid> FieldAsync(Fixture f)
    {
        f.Db.Series.Add(new() { Id = 1, Name = "Synthetic Series" });
        f.Db.Seasons.Add(new() { Id = 1, SeriesId = 1, Active = true });
        f.Db.CarClasses.Add(new() { Id = 1, Name = "Synthetic Class", ShortName = "SC" });
        var weekId = Guid.NewGuid();
        f.Db.Weeks.Add(new() { Id = weekId, SeasonId = 1, TrackId = 42, StartDate = new(2026, 10, 6) });
        await f.Db.SaveChangesAsync(Ct);
        var copies = new EvidenceCopyLifecycle(f.Db, f.Clock, f.Journal);
        var purpose = await copies.OpenSyntheticPurposeAsync(EvidencePurposeKind.IndependentOfficial, 1, ct: Ct);
        var receipt = await copies.CaptureAsync(purpose, EvidenceCopyKind.OfficialField, "synthetic-private-field", ct: Ct);
        await copies.CommitAsync(receipt, new OfficialFieldBatch(new()
        {
            Id = 1,
            SeasonId = 1,
            WeekId = weekId,
            TrackId = 42,
            OfficialSession = true,
            StartTime = f.Clock.Now
        }, [
            new() { SubsessionId = 1, CustId = f.Scope.CustomerId, CarId = 99, CarClassId = 1, BestLapSeconds = 105 },
            new() { SubsessionId = 1, CustId = 22222, CarId = 99, CarClassId = 1, BestLapSeconds = 100 },
            new() { SubsessionId = 1, CustId = 33333, CarId = 99, CarClassId = 1, BestLapSeconds = 110 }]), Ct);
        return weekId;
    }

    [Fact]
    public async Task ExplicitFreshCollectionAfterRecoveryCutoffCannotReactivateDeletionDueCopies()
    {
        await using var f = await CreateAsync();
        await f.GrantAsync();
        await f.UploadAsync(90);
        var loss = await f.Authority.TransitionAsync(f.Scope, DriverLifecycleKind.Unlink, Guid.NewGuid(), Ct);
        f.Clock.Now = loss.OriginalLossAt.AddDays(90);
        f.Proof.Receipt = f.Proof.Receipt! with { ReceiptId = Guid.NewGuid(), VerifiedAt = f.Clock.Now };
        var fresh = await f.Authority.GrantFreshCollectionAsync(f.Scope, Personal, Ct);
        Assert.Empty(await f.Uploads.ReadBestsAsync(fresh, Ct));
        await f.UploadAsync(85);
        Assert.Equal(85, Assert.Single(await f.Uploads.ReadBestsAsync(fresh, Ct)).BestLapSeconds);
        await new EvidenceCopyLifecycle(f.Db, f.Clock).ReconcileAsync(Ct);
        Assert.Single(await f.Db.PrivateUploadSessions.ToListAsync(Ct));
        var original = await f.Db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(c => c.Kind == EvidenceCopyKind.PrivateUpload && c.UnavailableAt != null, Ct);
        Assert.Equal(loss.OriginalLossAt.AddDays(97), original.RemovalDueAt);
    }

    [Fact]
    public async Task FreshGrantCannotRelabelExpiredDormantSourcesThroughRawWriter()
    {
        await using var f = await CreateAsync();
        await f.GrantAsync();
        await f.UploadAsync(90);
        var session = await f.Db.PrivateUploadSessions.AsNoTracking().SingleAsync(Ct);
        var loss = await f.Authority.TransitionAsync(f.Scope, DriverLifecycleKind.Unlink, Guid.NewGuid(), Ct);
        f.Clock.Now = loss.OriginalLossAt.AddDays(90);
        f.Proof.Receipt = f.Proof.Receipt! with { ReceiptId = Guid.NewGuid(), VerifiedAt = f.Clock.Now };
        var owner = await f.Authority.GrantFreshCollectionAsync(f.Scope, Personal, Ct);
        var receipt = await f.Uploads.CaptureAsync(owner, Ct);
        await using (var raw = f.Restart())
        await using (var transaction = await raw.Database.BeginTransactionAsync(Ct))
        {
            var old = await raw.EvidenceCopyMarkers.AsNoTracking().SingleAsync(c => c.Id == session.EvidenceCopyId, Ct);
            var forged = new EvidenceCopyMarker
            {
                Id = Guid.NewGuid(),
                PurposeId = receipt.PurposeId,
                Kind = EvidenceCopyKind.PrivateUpload,
                Provenance = DataProvenance.Demo,
                Generation = receipt.Generation,
                Version = old.Version + 1,
                OriginalAcquiredAt = old.OriginalAcquiredAt,
                KeyHash = old.KeyHash
            };
            raw.EvidenceCopyMarkers.Add(forged);
            await raw.SaveChangesAsync(Ct);
            var rejected = await Assert.ThrowsAsync<PostgresException>(() => raw.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE iracing.\"PrivateUploadSessions\" SET \"EvidenceCopyId\" = {forged.Id} WHERE \"Id\" = {session.Id}", Ct));
            Assert.Equal(PostgresErrorCodes.CheckViolation, rejected.SqlState);
            await transaction.RollbackAsync(Ct);
        }
        Assert.Empty(await f.Uploads.ReadBestsAsync(owner, Ct));
        Assert.Equal(session.EvidenceCopyId, (await f.Db.PrivateUploadSessions.AsNoTracking().SingleAsync(Ct)).EvidenceCopyId);
    }

    private sealed class Fault(string boundary) : IDriverLifecycleObserver
    {
        public Task PhaseAsync(string phase, Guid operationId, CancellationToken ct = default) => phase == boundary
            ? Task.FromException(new InvalidOperationException("Controlled interruption.")) : Task.CompletedTask;
    }

    private sealed class Fixture(DbContextOptions<AppDbContext> options) : IAsyncDisposable
    {
        public AppDbContext Db { get; } = new(options, new IRacingDataScope(DataProvenance.Demo));
        public DriverScope Scope { get; } = new(Guid.NewGuid(), 12345, DataProvenance.Demo);
        public Clock Clock { get; } = new();
        public Journal Journal { get; } = new();
        public Proof Proof { get; } = new();
        public DriverAuthorityStore Store => new(Db, Clock);
        public DriverAuthorization Authority => new(Store, Journal, Proof, Clock);
        public PrivateUploadStore Uploads => new(Db, Clock, Journal);
        public TelemetryUploadService Service => new(Db, Authority, Uploads);
        public AppDbContext Restart() => new(options, new IRacingDataScope(DataProvenance.Demo));
        public Task<DriverAccess> GrantAsync()
        {
            Proof.Receipt ??= new(Guid.NewGuid(), Scope, Clock.Now.AddDays(-1), "controlled-synthetic", "Synthetic Driver");
            return Authority.GrantAsync(Proof.Receipt.Scope, Personal, Ct);
        }
        public Task<PrivateUploadOutcome> UploadAsync(float seconds) => Service.ProcessSyntheticAsync(
            FakeIbtBuilder.Build(laps: 2, lapTime: seconds, customerId: Scope.CustomerId, sessionDate: Clock.Now.ToUnixTimeSeconds()), Scope, Ct);
        public PrivateUploadData Data(double seconds, int days) => new(99, 42, Clock.Now.AddDays(days), LapSessionType.Unknown, [new(1, seconds)]);
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Proof : IDriverOwnershipProof
    {
        public VerifiedDriverProof? Receipt { get; set; }
        public Task<VerifiedDriverProof?> VerifyAsync(DriverScope scope, CancellationToken ct = default) => Task.FromResult(Receipt);
    }
    private sealed class Journal : IDriverEnforcementJournal
    {
        public Dictionary<Guid, DriverLifecycleIntent> Intents { get; } = [];
        private readonly Dictionary<Guid, long> completed = [];
        public Task<DriverUserEnforcement> ReadUserAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(
            new DriverUserEnforcement(true, Intents.Values.FirstOrDefault(i => i.Scope.UserId == userId && i.Kind == DriverLifecycleKind.DeleteUser)));
        public Task<DriverJournalState> ReadAsync(DriverScope scope, CancellationToken ct = default) => Task.FromResult(
            new DriverJournalState(true, Intents.Values.Where(i => i.Scope == scope && completed.ContainsKey(i.OperationId))
                .Select(i => completed[i.OperationId]).DefaultIfEmpty().Max(), Intents.Values.Where(i => i.Scope == scope && !completed.ContainsKey(i.OperationId)).ToList()));
        public Task<DriverLifecycleIntent> AppendAsync(DriverLifecycleIntent intent, CancellationToken ct = default)
        {
            if (!Intents.TryGetValue(intent.OperationId, out var original)) Intents.Add(intent.OperationId, original = intent);
            if (original.Scope != intent.Scope || original.GrantId != intent.GrantId || original.Kind != intent.Kind) throw new InvalidOperationException("Operation mismatch.");
            return Task.FromResult(original);
        }
        public Task ReconcileAsync(DriverLifecycleIntent intent, long revision, CancellationToken ct = default)
        { completed[intent.OperationId] = revision; return Task.CompletedTask; }
    }
}
