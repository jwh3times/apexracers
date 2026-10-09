using System.Net;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using ApexRacers.Tests.References;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace ApexRacers.Tests.Restore;

[Collection(PostgreSqlCollection.Name)]
public sealed class DriverReferenceRestoreTests(PostgreSqlFixture postgres)
{
    [Fact]
    public Task Restored_primary_imports_prior_pending_and_dispatched_accounting_before_actual_JWT_output() =>
        ReferenceTopology.Run(postgres, "browser-restore-release-history", async test =>
        {
            var ct = test.Token;
            var snapshot = await PrimarySnapshot.CaptureAsync(postgres, test.PrimaryConnection, ct);
            var passed = false;
            try
            {
                using (var prior = await test.Request("/api/drivers/scoped/personal", "prior"))
                {
                    Assert.Equal(HttpStatusCode.OK, prior.StatusCode);
                    Assert.Contains("Synthetic Reference Owner", await prior.Content.ReadAsStringAsync(ct));
                    await test.Wait("prior", "checkpointed");
                }
                await test.Control("hold/pending/admitted");
                var pending = test.Request("/api/drivers/scoped/personal", "pending");
                await test.Wait("pending", "admitted");
                await test.Control("hold/dispatch/first-written", secondHost: true);
                using var dispatched = await test.Request("/api/drivers/scoped/personal", "dispatch", secondHost: true);
                Assert.Equal(HttpStatusCode.OK, dispatched.StatusCode);
                await test.Wait("dispatch", "first-written", secondHost: true);
                var original = (await test.History.ReadAsync(ct)).Releases;
                Assert.Equal(3, original.Length);
                Assert.True(original[0].Terminal); Assert.False(original[0].ProvenUnsent);
                Assert.False(original[1].DispatchStarted); Assert.False(original[1].Terminal);
                Assert.True(original[2].DispatchStarted); Assert.False(original[2].Terminal);
                await snapshot.RestoreAsync(postgres, test.PrimaryConnection, ct);
                using (var closed = await test.Request("/api/drivers/scoped/personal", "before-history")) await DeniedAsync(closed, ct);
                await using (var db = test.Db())
                {
                    Assert.Empty(await db.Set<PublicationRelease>().ToListAsync(ct));
                    Assert.True(await new PublicationReleaseStore(db, TimeProvider.System, test.History, new BrowserCompositionReview(), test.HistoryEpoch).ReconcileHistoryAsync(ct));
                    var imported = await db.Set<PublicationRelease>().OrderBy(r => r.Sequence).ToArrayAsync(ct);
                    Assert.Equal(original.Select(r => r.Proposal.Id), imported.Select(r => r.Id));
                    Assert.Equal(original.Select(r => r.ReservedAt), imported.Select(r => r.ReservedAt));
                    Assert.NotNull(imported[0].TerminalAt); Assert.Null(imported[1].TerminalAt); Assert.Null(imported[2].TerminalAt);
                    Assert.False(imported[0].ProvenUnsent); Assert.False(imported[2].ProvenUnsent);
                }
                await test.Control("release/pending/admitted");
                using (var resumed = await pending)
                {
                    Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
                    Assert.Contains("Synthetic Reference Owner", await resumed.Content.ReadAsStringAsync(ct));
                    await test.Wait("pending", "checkpointed");
                }
                await test.Control("release/dispatch/first-written", secondHost: true);
                Assert.Contains("Synthetic Reference Owner", await dispatched.Content.ReadAsStringAsync(ct));
                await test.Wait("dispatch", "checkpointed", secondHost: true);
                var complete = (await test.History.ReadAsync(ct)).Releases;
                Assert.All(complete, r => { Assert.True(r.Terminal); Assert.False(r.ProvenUnsent); });
                using (var useful = await test.Request("/api/drivers/scoped/personal", "after-history"))
                {
                    Assert.Equal(HttpStatusCode.OK, useful.StatusCode);
                    Assert.Contains("89.9", await useful.Content.ReadAsStringAsync(ct));
                    await test.Wait("after-history", "checkpointed");
                }
                await using (var independent = new NpgsqlConnection(test.IndependentConnection))
                {
                    await independent.OpenAsync(ct);
                    await using var remove = new NpgsqlCommand("DELETE FROM publication_history WHERE sequence = (SELECT MAX(sequence) FROM publication_history)", independent);
                    Assert.Equal(1, await remove.ExecuteNonQueryAsync(ct));
                }
                Assert.False((await test.History.ReadAsync(ct)).Available);
                using (var closed = await test.Request("/api/drivers/scoped/personal", "missing-history")) await DeniedAsync(closed, ct);
                using (var closed = await test.Request("/api/drivers/scoped/discovery", "missing-discovery", secondHost: true)) await DeniedAsync(closed, ct);
                await using (var db = test.Db())
                {
                    var store = new PublicationReleaseStore(db, TimeProvider.System, test.History, new BrowserCompositionReview(), test.HistoryEpoch);
                    Assert.False(await store.ReconcileHistoryAsync(ct));
                    Assert.Null(await store.ReserveAsync(original[0].Proposal with { Id = Guid.NewGuid(), CatalogId = "fresh-catalog-must-not-reset" }, _ => Task.FromResult(true), test.History, ct));
                    Assert.Null(await store.ReserveAsync(original[0].Proposal with { Id = Guid.NewGuid(), Provenance = DataProvenance.Real }, _ => Task.FromResult(true), test.History, ct));
                }
                passed = true;
            }
            finally
            {
                await snapshot.RecordAsync("reference-release-history", new
                {
                    Accounting = "prior terminal, pending before dispatch, actual possibly dispatched HTTP writer",
                    UsefulRecovery = true,
                    Topology = "Two JWT/product-controller Kestrel hosts; physical primary snapshot; separate current enforcement and release-history PostgreSQL"
                }, passed);
            }
        });

    [Fact]
    public Task Restored_profiles_opaque_references_and_follows_cannot_revive_a_deleted_Target() =>
        ReferenceTopology.Run(postgres, "browser-restore-target-deletion", async test =>
        {
            var ct = test.Token;
            var driver = await test.Discover("before-delete-discovery");
            using (var followed = await test.Request("/api/drivers/scoped/follows", "before-delete-follow", reference: driver.FollowReference))
            { Assert.Equal(HttpStatusCode.OK, followed.StatusCode); await followed.Content.ReadAsStringAsync(ct); await test.Wait("before-delete-follow", "checkpointed"); }
            var snapshot = await PrimarySnapshot.CaptureAsync(postgres, test.PrimaryConnection, ct);
            var passed = false;
            DriverLifecycleOutcome? loss = null;
            try
            {
                loss = await test.Transition(ReferenceActors.Target, DriverLifecycleKind.DeleteUser);
                Assert.True(loss.Completed);
                await snapshot.RestoreAsync(postgres, test.PrimaryConnection, ct);
                await using (var db = test.Db())
                {
                    Assert.True(await db.Users.AnyAsync(u => u.Id == ReferenceActors.Target, ct));
                    Assert.NotEmpty(await db.Set<ScopedDriverReference>().ToListAsync(ct));
                    Assert.True(Assert.Single(await db.Set<PrivateDriverFollow>().ToListAsync(ct)).Active);
                }
                using (var closed = await test.Request("/api/drivers/scoped/comparison", "restored-comparison", reference: driver.ComparisonReference)) await DeniedAsync(closed, ct);
                using (var closed = await test.Request("/api/drivers/scoped/follows", "restored-follow", reference: driver.FollowReference)) await DeniedAsync(closed, ct);
                using (var closed = await test.Request("/api/drivers/scoped/personal", "restored-target", actor: ReferenceActors.Target)) await DeniedAsync(closed, ct);
                var clock = new ReferenceClock(); clock.Set(loss.OriginalLossAt.AddDays(8));
                await using (var db = test.Db())
                {
                    var store = new DriverAuthorityStore(db, clock);
                    var grant = await db.DriverAuthorizationGrants.SingleAsync(g => g.UserId == ReferenceActors.Target, ct);
                    var authority = new ApexRacers.Api.Services.DriverAuthorization(store, test.History, new UnavailableDriverOwnershipProof(), clock);
                    Assert.True((await authority.RecoverAsync(new(loss.OperationId, grant.Id, ReferenceActors.Scope(ReferenceActors.Target), DriverLifecycleKind.DeleteUser, loss.OriginalLossAt), ct)).Completed);
                    await new EvidenceCopyLifecycle(db, clock, test.History).ReconcileAsync(ct);
                    await new DriverReferenceStore(db, clock, test.History).ReconcileAsync(ct);
                    Assert.False(await db.Users.AnyAsync(u => u.Id == ReferenceActors.Target, ct));
                    Assert.Empty(await db.Set<ScopedDriverReference>().ToListAsync(ct));
                    Assert.Empty(await db.Set<PrivateDriverFollow>().ToListAsync(ct));
                    Assert.Equal(loss.OriginalLossAt, (await db.DriverLifecycleOperations.SingleAsync(o => o.Id == loss.OperationId, ct)).OriginalLossAt);
                    Assert.True(await new PublicationReleaseStore(db, clock, test.History, new BrowserCompositionReview(), test.HistoryEpoch).ReconcileHistoryAsync(ct));
                }
                using (var own = await test.Request("/api/drivers/scoped/personal", "unaffected-recipient"))
                { Assert.Equal(HttpStatusCode.OK, own.StatusCode); Assert.Contains("Synthetic Reference Owner", await own.Content.ReadAsStringAsync(ct)); await test.Wait("unaffected-recipient", "checkpointed"); }
                passed = true;
            }
            finally { await snapshot.RecordAsync("reference-target-deletion", new { Loss = loss, Profile = "primary grant/name restored", Copies = "opaque references and private Follow physically removed after canonical deletion replay", UnaffectedRecipientUseful = true }, passed); }
        });

    private static async Task DeniedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.DoesNotContain("Synthetic Reference", body); Assert.DoesNotContain("officialBestLapSeconds", body);
    }
}
