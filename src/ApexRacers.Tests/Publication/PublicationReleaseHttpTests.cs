using System.Collections.Immutable;
using System.Net;
using System.Text.Json;
using ApexRacers.Api.Services;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace ApexRacers.Tests.Publication;

[Collection(PostgreSqlCollection.Name)]
public sealed class PublicationReleaseHttpTests(PostgreSqlFixture fixture)
{
    [Theory]
    [InlineData("owner")]
    [InlineData("owner2")]
    public Task EachExplicitOwnerVariantIsUsefulAlone(string actor) =>
        PublicationLedgerTopology.Run(fixture, "positive-" + actor, async t =>
        {
            using var response = await t.Read("owner", PublicationPurpose.Owner, actor, offset: 1);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(t.Token));
            Assert.Empty(body.RootElement.GetProperty("hiddenGroups").EnumerateArray());
            Assert.Equal(92.85714285714286, body.RootElement.GetProperty("ownerAnalytics").GetProperty("percentileRank").GetDouble());
            await t.Wait("owner", "checkpointed");
            Assert.False(Assert.Single((await t.History.ReadAsync(t.Token)).Releases).ProvenUnsent);
        });

    [Theory]
    [InlineData("before-admission")]
    [InlineData("admitted")]
    public Task FirstHiddenSharingGrantCannotBypassAbsenceDependencyOrActiveWriterDrain(string phase) =>
        PublicationLedgerTopology.Run(fixture, "first-hidden-grant-" + phase, async t =>
        {
            var newUser = Guid.NewGuid();
            await using (var db = t.Db())
            {
                db.Users.Add(new ApplicationUser { Id = newUser, DisplayName = "Synthetic new hidden owner", EmailConfirmed = true });
                await db.SaveChangesAsync(t.Token);
            }
            await t.Control("hold/old/" + phase);
            var request = t.Read("old", PublicationPurpose.Owner, "owner");
            await t.Wait("old", phase);
            async Task Grant()
            {
                await using var db = t.Db();
                await new DriverAuthorityStore(db, TimeProvider.System).GrantAsync(new(Guid.NewGuid(), new(newUser, 3, DataProvenance.Demo),
                    DateTimeOffset.UtcNow.AddSeconds(-1), "controlled-synthetic-ledger-proof-v1", "Synthetic newly sharing Driver"),
                    new(DriverAuthorizationPolicy.PersonalConsentVersion, DriverAuthorizationPolicy.SharingConsentVersion), t.History, t.Token);
            }
            if (phase == "admitted")
            {
                await Assert.ThrowsAsync<InvalidOperationException>(Grant);
                await using var db = t.Db();
                Assert.Null(await db.DriverAuthorizationGrants.SingleOrDefaultAsync(g => g.CustomerId == 3, t.Token));
                await Assert.ThrowsAsync<PostgresException>(() => db.DriverAuthorizationGrants.Where(g => g.CustomerId == 1)
                    .ExecuteUpdateAsync(s => s.SetProperty(g => g.Revision, g => g.Revision + 1), t.Token));
            }
            else await Grant();
            await t.Control("release/old/" + phase);
            using var response = await request;
            if (phase == "admitted")
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                await response.Content.ReadAsStringAsync(t.Token);
                await t.Wait("old", "checkpointed");
                await Grant();
            }
            else await PublicationLedgerTopology.Denied(response, t.Token);
            using var unknown = await t.Read("unknown", PublicationPurpose.Owner, "owner", secondHost: true);
            await PublicationLedgerTopology.Denied(unknown, t.Token);
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task CheckpointFaultAndActualPostgreSqlSessionLossRetainPossibleOutput(bool sessionLoss) =>
        PublicationLedgerTopology.Run(fixture, "checkpoint-fault-" + sessionLoss, async t =>
        {
            if (sessionLoss) await t.Control("hold/live/checkpoint-command");
            else await t.Control("fault/history-checkpoint-before");
            using var response = await t.Read("live", PublicationPurpose.Owner, "owner");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            await response.Content.ReadAsStringAsync(t.Token);
            await t.Wait("live", "transport-ended");
            if (sessionLoss)
            {
                await t.Wait("live", "checkpoint-command");
                using var backendResponse = await t.First.ControlClient.GetAsync("/control/backend/live", t.Token);
                var backend = int.Parse(await backendResponse.Content.ReadAsStringAsync(t.Token));
                await using var db = t.Db();
                await db.Database.OpenConnectionAsync(t.Token);
                await using var terminate = new NpgsqlCommand("SELECT pg_terminate_backend(@backend)", (NpgsqlConnection)db.Database.GetDbConnection());
                terminate.Parameters.AddWithValue("backend", backend);
                Assert.True((bool)(await terminate.ExecuteScalarAsync(t.Token))!);
                await t.Control("release/live/checkpoint-command");
            }
            await t.Wait("live", "request-ended");
            if (!sessionLoss) await t.Control("clear/history-checkpoint-before");
            await using (var db = t.Db()) Assert.Null(Assert.Single(await db.Set<PublicationRelease>().ToArrayAsync(t.Token)).TerminalAt);
            var possible = Assert.Single((await t.History.ReadAsync(t.Token)).Releases);
            Assert.True(possible.DispatchStarted);
            Assert.False(possible.ProvenUnsent);
            using var denied = await t.Read("visitor", secondHost: true);
            await PublicationLedgerTopology.Denied(denied, t.Token);
            await t.Control("retry-checkpoint/live");
            Assert.True(Assert.Single((await t.History.ReadAsync(t.Token)).Releases).Terminal);
        });

    [Fact]
    public Task UsefulExplicitlyReviewedVisitorAndSignedInCompositionPublishesThroughTwoHosts() =>
        PublicationLedgerTopology.Run(fixture, "reviewed-positive", async t =>
        {
            using var visitor = await t.Read("visitor");
            Assert.True(visitor.StatusCode == HttpStatusCode.OK, await visitor.Content.ReadAsStringAsync(t.Token));
            Assert.Equal("no-store", visitor.Headers.CacheControl?.ToString());
            var visitorBody = await visitor.Content.ReadAsStringAsync(t.Token);
            Assert.Contains("lowerInclusive", visitorBody);
            Assert.DoesNotContain("Synthetic Consenting Driver", visitorBody);
            await t.Wait("visitor", "checkpointed");
            using var signed = await t.Read("signed", PublicationPurpose.SignedIn, secondHost: true);
            Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
            Assert.Contains("Synthetic Consenting Driver", await signed.Content.ReadAsStringAsync(t.Token));
            await t.Wait("signed", "checkpointed", true);
            var history = await t.History.ReadAsync(t.Token);
            Assert.Equal(2, history.Releases.Length);
            Assert.All(history.Releases, r => { Assert.True(r.Terminal); Assert.False(r.ProvenUnsent); });
            await using var db = t.Db();
            Assert.Equal(2, await db.Set<PublicationRelease>().CountAsync(t.Token));
        });

    [Theory]
    [InlineData(PublicationPurpose.Aggregate, "recipient")]
    [InlineData(PublicationPurpose.Owner, "owner2")]
    public Task AtomicPendingReservationMakesOnlyOneIndividuallySafeCompositionWin(PublicationPurpose competing, string actor) =>
        PublicationLedgerTopology.Run(fixture, "concurrent-" + competing, async t =>
        {
            await t.Control("hold/first/admitted");
            var first = t.Read("first", PublicationPurpose.Owner, "owner");
            await t.Wait("first", "admitted");
            using var loser = await t.Read("second", competing, actor, offset: 1, secondHost: true);
            await PublicationLedgerTopology.Denied(loser, t.Token);
            Assert.Single((await t.History.ReadAsync(t.Token)).Releases);
            await t.Control("release/first/admitted");
            using var winner = await first;
            Assert.Equal(HttpStatusCode.OK, winner.StatusCode);
            await winner.Content.ReadAsStringAsync(t.Token);
            await t.Wait("first", "checkpointed");
            using var after = await t.Read("after", competing, actor, secondHost: true);
            await PublicationLedgerTopology.Denied(after, t.Token);
        });

    [Theory]
    [InlineData("before-admission", "same-band")]
    [InlineData("admitted", "same-band")]
    [InlineData("before-admission", "same-values-new-copy")]
    [InlineData("admitted", "same-values-new-copy")]
    [InlineData("before-admission", "catalog-revision")]
    [InlineData("admitted", "catalog-revision")]
    [InlineData("before-admission", "provenance")]
    [InlineData("admitted", "provenance")]
    [InlineData("before-admission", "copy-purpose")]
    [InlineData("admitted", "copy-purpose")]
    [InlineData("before-admission", "sharing-closure")]
    [InlineData("admitted", "sharing-closure")]
    public Task CompleteCurrentDependenciesInvalidateAllUnsentWork(string phase, string mutation) =>
        PublicationLedgerTopology.Run(fixture, "stale-" + phase + "-" + mutation, async t =>
        {
            await t.Control("hold/old/" + phase);
            var pending = t.Read("old", PublicationPurpose.SignedIn);
            await t.Wait("old", phase);
            if (mutation == "sharing-closure") await t.Transition(7);
            else if (mutation == "copy-purpose")
            {
                await using var db = t.Db();
                var purpose = await db.EvidencePurposes.SingleAsync(p => p.Kind == EvidencePurposeKind.SyntheticPreview, t.Token);
                await new EvidenceCopyLifecycle(db, TimeProvider.System).EndPurposeAsync(purpose.Id, DateTimeOffset.UtcNow, t.Token);
            }
            else await t.ChangeEvidence(source => mutation switch
            {
                "same-band" => source with
                {
                    Snapshot = source.Snapshot with
                    {
                        Members = source.Snapshot.Members.SetItem(0,
                    source.Snapshot.Members[0] with { LapSeconds = new(CandidateMeasurementState.Measured, 90.101m) })
                    }
                },
                "catalog-revision" => source with { Snapshot = source.Snapshot with { CatalogRevision = 2 } },
                "same-values-new-copy" => source,
                _ => source with { Snapshot = source.Snapshot with { Provenance = DataProvenance.Real } }
            });
            await t.Control("release/old/" + phase);
            using var response = await pending;
            await PublicationLedgerTopology.Denied(response, t.Token);
            if (phase == "admitted")
            {
                await t.Wait("old", "checkpointed");
                var row = Assert.Single((await t.History.ReadAsync(t.Token)).Releases);
                Assert.True(row.ProvenUnsent);
                Assert.False(row.DispatchStarted);
            }
            else Assert.Empty((await t.History.ReadAsync(t.Token)).Releases);
            using var freshUnknown = await t.Read("fresh", PublicationPurpose.SignedIn, secondHost: true);
            await PublicationLedgerTopology.Denied(freshUnknown, t.Token);
        });

    [Theory]
    [InlineData("scope-mismatch")]
    [InlineData("forged-name")]
    [InlineData("catalog-unreviewed")]
    [InlineData("separate-journal")]
    public Task UnknownMalformedOrUnpairedControlledContextsStayClosed(string fault) =>
        PublicationLedgerTopology.Run(fixture, "closed-" + fault, async t =>
        {
            await t.Control("fault/" + fault);
            using var denied = await t.Read("denied", PublicationPurpose.SignedIn);
            await PublicationLedgerTopology.Denied(denied, t.Token);
            Assert.Empty((await t.History.ReadAsync(t.Token)).Releases);
        });

    [Fact]
    public Task ActualRecipientAndPurposeAndCurrentCatalogVariantCannotBeSubstituted() =>
        PublicationLedgerTopology.Run(fixture, "recipient-catalog-bindings", async t =>
        {
            using var missing = await t.Read("missing", PublicationPurpose.SignedIn, "missing");
            await PublicationLedgerTopology.Denied(missing, t.Token);
            using var ownerMismatch = await t.Read("owner-mismatch", PublicationPurpose.Owner);
            await PublicationLedgerTopology.Denied(ownerMismatch, t.Token);
            using var undeclaredPage = await t.Read("undeclared-page", offset: 2);
            await PublicationLedgerTopology.Denied(undeclaredPage, t.Token);
            await t.Control("hold/owner/admitted");
            var owner = t.Read("owner", PublicationPurpose.Owner, "owner");
            await t.Wait("owner", "admitted");
            await t.Control("fault/catalog-owner-removed");
            await t.Control("release/owner/admitted");
            using var denied = await owner;
            await PublicationLedgerTopology.Denied(denied, t.Token);
            await t.Wait("owner", "checkpointed");
            Assert.True(Assert.Single((await t.History.ReadAsync(t.Token)).Releases).ProvenUnsent);
            await t.Control("clear/catalog-owner-removed");
            using var visitor = await t.Read("visitor-after-unsent", secondHost: true);
            Assert.Equal(HttpStatusCode.OK, visitor.StatusCode);
            await visitor.Content.ReadAsStringAsync(t.Token);
            await t.Wait("visitor-after-unsent", "checkpointed", true);
            var retained = (await t.History.ReadAsync(t.Token)).Releases;
            Assert.Equal(2, retained.Length);
            Assert.True(retained[0].ProvenUnsent);
            Assert.False(retained[1].ProvenUnsent);
        });

    [Theory]
    [InlineData("before-admission")]
    [InlineData("admitted")]
    public Task EntireReviewedCatalogIsCurrentEvenWhenRequestedOwnerProjectionDoesNotChange(string phase) =>
        PublicationLedgerTopology.Run(fixture, "other-owner-catalog-change-" + phase, async t =>
        {
            var normalized = CohortActors.Source();
            var all = WholeCohortCandidates.ReviewControlledSynthetic(normalized.Snapshot, normalized.ReviewedVariants, new(
                CandidateRiskVerdict.ControlledSyntheticSupported, CandidateRiskVerdict.ControlledSyntheticSupported,
                CandidateRiskVerdict.ControlledSyntheticSupported, CandidateRiskVerdict.ControlledSyntheticSupported));
            var subset = WholeCohortCandidates.ReviewControlledSynthetic(normalized.Snapshot,
                normalized.ReviewedVariants.Where(v => v.OwnerCustomerId != 2).ToImmutableArray(), new(
                    CandidateRiskVerdict.ControlledSyntheticSupported, CandidateRiskVerdict.ControlledSyntheticSupported,
                    CandidateRiskVerdict.ControlledSyntheticSupported, CandidateRiskVerdict.ControlledSyntheticSupported));
            var owner = normalized.ReviewedVariants.Single(v => v.OwnerCustomerId == 1);
            var oldProjection = WholeCohortCandidates.Prepare(WholeCohortCandidates.CatalogId, WholeCohortCandidates.Scope, normalized.Snapshot, all, owner);
            var newProjection = WholeCohortCandidates.Prepare(WholeCohortCandidates.CatalogId, WholeCohortCandidates.Scope, normalized.Snapshot, subset, owner);
            Assert.NotNull(oldProjection);
            Assert.NotNull(newProjection);
            Assert.Equal(JsonSerializer.Serialize(oldProjection.Projection), JsonSerializer.Serialize(newProjection.Projection));
            await t.Control("hold/owner/" + phase);
            var request = t.Read("owner", PublicationPurpose.Owner, "owner");
            await t.Wait("owner", phase);
            await t.Control("fault/catalog-other-owner-removed");
            await t.Control("release/owner/" + phase);
            using var denied = await request;
            await PublicationLedgerTopology.Denied(denied, t.Token);
            if (phase == "before-admission") Assert.Empty((await t.History.ReadAsync(t.Token)).Releases);
            else
            {
                await t.Wait("owner", "checkpointed");
                Assert.True(Assert.Single((await t.History.ReadAsync(t.Token)).Releases).ProvenUnsent);
            }
            await t.Control("fault/catalog-other-owner-removed", secondHost: true);
            using var freshUnknown = await t.Read("fresh-unknown", PublicationPurpose.Owner, "owner", secondHost: true);
            await PublicationLedgerTopology.Denied(freshUnknown, t.Token);
        });

    [Fact]
    public Task CurrentSignedInRecipientErasureBlocksAdmissionAndKnownPendingRecipientErasureIsFenced() =>
        PublicationLedgerTopology.Run(fixture, "recipient-erasure", async t =>
        {
            await t.Control("hold/signed/admitted");
            var pending = t.Read("signed", PublicationPurpose.SignedIn);
            await t.Wait("signed", "admitted");
            await using (var db = t.Db())
                await Assert.ThrowsAsync<PostgresException>(() => db.Users.Where(u => u.Id == CohortActors.Recipient).ExecuteDeleteAsync(t.Token));
            await t.Control("release/signed/admitted");
            using var first = await pending;
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            await first.Content.ReadAsStringAsync(t.Token);
            await t.Wait("signed", "checkpointed");
            await t.Control("hold/old/before-admission");
            var old = t.Read("old", PublicationPurpose.SignedIn);
            await t.Wait("old", "before-admission");
            await using (var db = t.Db()) await db.Users.Where(u => u.Id == CohortActors.Recipient).ExecuteDeleteAsync(t.Token);
            await t.Control("release/old/before-admission");
            using var denied = await old;
            await PublicationLedgerTopology.Denied(denied, t.Token);
        });

    [Theory]
    [InlineData("history-reserve-after")]
    [InlineData("history-dispatch-after")]
    [InlineData("history-dispatch-read-unavailable")]
    public Task IndependentAndDispatchUncertaintyNeverBecomesAbsentOutput(string fault) =>
        PublicationLedgerTopology.Run(fixture, "uncertain-" + fault, async t =>
        {
            await t.Control("fault/" + fault);
            try { using var response = await t.Read("uncertain", PublicationPurpose.Owner, "owner"); Assert.NotEqual(HttpStatusCode.OK, response.StatusCode); }
            catch (HttpRequestException) { }
            var entry = Assert.Single((await t.History.ReadAsync(t.Token)).Releases);
            Assert.False(entry.ProvenUnsent);
            if (fault != "history-reserve-after") Assert.True(entry.DispatchStarted);
            using var denied = await t.Read("other", secondHost: true);
            await PublicationLedgerTopology.Denied(denied, t.Token);
            await t.Reconcile();
            using var stillDenied = await t.Read("other-after-reconcile", secondHost: true);
            await PublicationLedgerTopology.Denied(stillDenied, t.Token);
        });

    [Fact]
    public Task LostPrimaryHistoryRestoresConservativelyAndCannotAcknowledgeLiveExecutorClosure() =>
        PublicationLedgerTopology.Run(fixture, "actual-primary-snapshot-restore", async t =>
        {
            var snapshot = await t.Backup();
            await t.Control("hold/live/first-written");
            using var visible = await t.Read("live", PublicationPurpose.Owner, "owner");
            Assert.Equal(HttpStatusCode.OK, visible.StatusCode);
            await t.Wait("live", "first-written");
            var stream = await visible.Content.ReadAsStreamAsync(t.Token);
            var bytes = new byte[32];
            Assert.True(await stream.ReadAsync(bytes, t.Token) > 0);
            await t.Restore(snapshot);
            using var denied = await t.Read("restored", secondHost: true);
            await PublicationLedgerTopology.Denied(denied, t.Token);
            var closure = await t.Transition();
            Assert.False(closure.Completed);
            Assert.True(closure.PendingWriters > 0);
            await t.Reconcile();
            await using (var db = t.Db()) Assert.Null(Assert.Single(await db.Set<PublicationRelease>().ToArrayAsync(t.Token)).TerminalAt);
            await t.Control("release/live/first-written");
            await stream.CopyToAsync(Stream.Null, t.Token);
            await t.Wait("live", "checkpointed");
            var history = Assert.Single((await t.History.ReadAsync(t.Token)).Releases);
            Assert.True(history.Terminal);
            Assert.False(history.ProvenUnsent);
            using var deniedComposition = await t.Read("visitor-after", secondHost: true);
            await PublicationLedgerTopology.Denied(deniedComposition, t.Token);
        });

    [Fact]
    public Task KilledWriterRestartCannotForgetPossibleHistoryOrCheckpointOldIncarnation() =>
        PublicationLedgerTopology.Run(fixture, "actual-writer-restart", async t =>
        {
            await t.Control("hold/live/first-written");
            using var response = await t.Read("live", PublicationPurpose.Owner, "owner");
            await t.Wait("live", "first-written");
            await t.RestartFirst();
            var entry = Assert.Single((await t.History.ReadAsync(t.Token)).Releases);
            Assert.False(entry.Terminal);
            await using (var db = t.Db())
                await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => t.Store(db).CheckpointAsync(entry with
                { Proposal = entry.Proposal with { Incarnation = t.First.Incarnation } }, false, t.Token));
            using var denied = await t.Read("visitor-new");
            await PublicationLedgerTopology.Denied(denied, t.Token);
            await using (var db = t.Db())
            {
                var future = new PublicationReleaseStore(db, new PublicationClock(DateTimeOffset.UtcNow.AddYears(1)),
                    t.History, new FiniteCompositionReview(), (await t.History.ReadAsync(t.Token)).Epoch);
                Assert.True(await future.ReconcileHistoryAsync(t.Token));
                Assert.Null(Assert.Single(await db.Set<PublicationRelease>().ToArrayAsync(t.Token)).TerminalAt);
            }
            Assert.False((await t.Transition()).Completed);
        });

    [Fact]
    public Task RestoredEmptyPrimaryCannotTrustFreshIndependentGenesis() =>
        PublicationLedgerTopology.Run(fixture, "fresh-history-after-empty-primary-restore", async t =>
        {
            var originalPrimary = await t.Backup();
            using var released = await t.Read("released");
            Assert.Equal(HttpStatusCode.OK, released.StatusCode);
            await released.Content.ReadAsStringAsync(t.Token);
            await t.Wait("released", "checkpointed");
            await t.Restore(originalPrimary);
            await using var connection = new NpgsqlConnection(t.IndependentConnection);
            await connection.OpenAsync(t.Token);
            await using var replace = new NpgsqlCommand("DELETE FROM publication_history; UPDATE publication_history_meta SET epoch=gen_random_uuid(), count=0, commitment=@empty", connection);
            replace.Parameters.AddWithValue("empty", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("[]"))));
            await replace.ExecuteNonQueryAsync(t.Token);
            Assert.False((await t.History.ReadAsync(t.Token)).Available);
            using var denied = await t.Read("unknown-empty", secondHost: true);
            await PublicationLedgerTopology.Denied(denied, t.Token);
            await using var db = t.Db();
            Assert.Empty(await db.Set<PublicationRelease>().ToArrayAsync(t.Token));
            Assert.False(await t.Store(db).ReconcileHistoryAsync(t.Token));
        });

    [Theory]
    [InlineData("trailing")]
    [InlineData("meta")]
    [InlineData("epoch")]
    public Task IndependentContinuityLossIsUnknownAndFreshCatalogCannotResetHistory(string loss) =>
        PublicationLedgerTopology.Run(fixture, "independent-loss-" + loss, async t =>
        {
            using var visible = await t.Read("old");
            Assert.Equal(HttpStatusCode.OK, visible.StatusCode);
            await visible.Content.ReadAsStringAsync(t.Token);
            await t.Wait("old", "checkpointed");
            await using var c = new NpgsqlConnection(t.IndependentConnection);
            await c.OpenAsync(t.Token);
            await using var command = new NpgsqlCommand(loss switch
            {
                "trailing" => "DELETE FROM publication_history",
                "meta" => "DELETE FROM publication_history_meta",
                _ => "UPDATE publication_history_meta SET epoch=gen_random_uuid()"
            }, c);
            await command.ExecuteNonQueryAsync(t.Token);
            Assert.False((await t.History.ReadAsync(t.Token)).Available);
            using var closed = await t.Read("closed", secondHost: true);
            await PublicationLedgerTopology.Denied(closed, t.Token);
            using var otherCatalog = await t.Read("new-catalog", secondHost: true, catalog: "synthetic-catalog-v2");
            await PublicationLedgerTopology.Denied(otherCatalog, t.Token);
            await using var db = t.Db();
            Assert.False(await t.Store(db).ReconcileHistoryAsync(t.Token));
        });

    private sealed class PublicationClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
