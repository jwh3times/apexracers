using System.Net;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ApexRacers.Api.Services;
using ApexRacers.Tests.Lifecycle;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using System.Text.Json;
using ApexRacers.Api.Dtos;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace ApexRacers.Tests.References;

[Collection(PostgreSqlCollection.Name)]
public sealed class DriverReferenceHttpTests(PostgreSqlFixture fixture)
{
    [Fact]
    public Task Eligible_discovery_detail_comparison_and_follow_use_actual_JWT_recipient_and_atomic_owned_dispatch() =>
        ReferenceTopology.Run(fixture, "useful-eligible", async test =>
        {
            var row = await test.Discover();
            Assert.Equal("Synthetic Reference Driver", row.DriverName);
            Assert.True(DriverReferences.Valid(row.DetailReference));
            Assert.NotEqual(row.DetailReference, row.ComparisonReference);
            Assert.NotEqual(row.ComparisonReference, row.FollowReference);
            using var detail = await test.Request("/api/drivers/scoped/detail", "detail", reference: row.DetailReference, secondHost: true);
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            var body = await detail.Content.ReadAsStringAsync(test.Token); Safe(body);
            var parsed = JsonSerializer.Deserialize<ScopedDriverDetailDto>(body, JsonSerializerOptions.Web)!;
            Assert.Equal(90.16, parsed.OfficialBestLapSeconds); Assert.Equal(1350, parsed.IRating);
            await test.Wait("detail", "checkpointed", true);
            using var comparison = await test.Request("/api/drivers/scoped/comparison", "comparison", reference: row.ComparisonReference);
            Assert.Equal(HttpStatusCode.OK, comparison.StatusCode);
            var compared = await comparison.Content.ReadFromJsonAsync<ScopedDriverComparisonDto>(test.Token);
            Assert.Equal(89.90, compared!.SubjectOfficialBestLapSeconds);
            Assert.Equal(90.16 - 89.90, compared.LapDeltaSeconds);
            await test.Wait("comparison", "checkpointed");
            using var follow = await test.Request("/api/drivers/scoped/follows", "follow", reference: row.FollowReference);
            Assert.Equal(HttpStatusCode.OK, follow.StatusCode);
            Assert.True((await follow.Content.ReadFromJsonAsync<ScopedDriverFollowDto>(test.Token))!.Followed);
            await test.Wait("follow", "checkpointed");
            var saved = await test.Discover("followed", follows: true);
            Assert.Equal(row.DriverName, saved.DriverName); Assert.NotEqual(row.DetailReference, saved.DetailReference);
            await using var db = test.Db();
            Assert.Single(await db.Set<PrivateDriverFollow>().ToListAsync(test.Token));
            var refs = await db.Set<ScopedDriverReference>().ToListAsync(test.Token);
            Assert.DoesNotContain(refs, r => r.TokenHash == row.DetailReference);
            Assert.Equal(5, (await test.History.ReadAsync(test.Token)).Releases.Length);
        });

    [Fact]
    public Task Invalid_nonexistent_ineligible_wrong_recipient_purpose_raw_ID_and_unavailable_outcomes_match() =>
        ReferenceTopology.Run(fixture, "generic-denial", async test =>
        {
            var row = await test.Discover();
            using var missing = await test.Request("/api/drivers/scoped/detail", "missing", reference: new string('A', 64));
            var generic = await Denied(missing, test.Token);
            foreach (var (reference, actor, path) in new[]
            {
                (row.DetailReference, ReferenceActors.Other, "detail"), (row.DetailReference, ReferenceActors.Target, "detail"),
                (row.DetailReference, ReferenceActors.Recipient, "comparison"), ("2", ReferenceActors.Recipient, "detail"),
                (row.ComparisonReference, ReferenceActors.Recipient, "follows")
            })
            {
                using var denied = await test.Request("/api/drivers/scoped/" + path, Guid.NewGuid().ToString("N"), actor, reference);
                Assert.Equal(generic, await Denied(denied, test.Token));
            }
            using var nonexistent = await test.Request("/api/drivers/scoped/discovery?term=does-not-exist", "no-target");
            Assert.Equal(generic, await Denied(nonexistent, test.Token));
            await test.Control("fault/reference-source-unavailable");
            using var unavailable = await test.Request("/api/drivers/scoped/detail", "source-unavailable", reference: row.DetailReference);
            Assert.Equal(generic, await Denied(unavailable, test.Token));
            await test.Control("clear/reference-source-unavailable");
            Assert.True((await test.Transition(ReferenceActors.Target, DriverLifecycleKind.WithdrawSharing)).Completed);
            using var ineligible = await test.Request("/api/drivers/scoped/discovery?term=Synthetic%20Reference%20Driver", "ineligible");
            Assert.Equal(generic, await Denied(ineligible, test.Token));
            using var visitor = await test.Request("/api/drivers/scoped/discovery", "visitor", anonymous: true);
            Assert.Equal(HttpStatusCode.Unauthorized, visitor.StatusCode);
            Assert.DoesNotContain(row.DriverName, await visitor.Content.ReadAsStringAsync(test.Token));
        });

    [Theory]
    [InlineData(-1, true)] [InlineData(0, false)] [InlineData(1, false)]
    public Task Reference_expiry_is_rechecked_before_at_after_fifteen_minutes_and_does_not_expire_consent(int offsetMicroseconds, bool eligible) =>
        ReferenceTopology.Run(fixture, "expiry-" + offsetMicroseconds, async test =>
        {
            var row = await test.Discover();
            await test.Time(row.ExpiresAt.AddTicks(offsetMicroseconds * 10L));
            using var result = await test.Request("/api/drivers/scoped/detail", "expiry", reference: row.DetailReference);
            if (eligible) { Assert.Equal(HttpStatusCode.OK, result.StatusCode); await result.Content.ReadAsStringAsync(test.Token); await test.Wait("expiry", "checkpointed"); }
            else await Denied(result, test.Token);
            await using var db = test.Db(); Assert.NotNull((await db.DriverAuthorizationGrants.SingleAsync(g => g.UserId == ReferenceActors.Target, test.Token)).SharingConsentVersion);
        });

    [Theory]
    [InlineData("before-admission")] [InlineData("admitted")]
    public Task Delayed_actual_executor_rechecks_expiry_and_current_authenticated_recipient(string phase) =>
        ReferenceTopology.Run(fixture, "delayed-expiry-" + phase, async test =>
        {
            var row = await test.Discover();
            await test.Control("hold/delayed/" + phase);
            var read = test.Request("/api/drivers/scoped/detail", "delayed", reference: row.DetailReference);
            await test.Wait("delayed", phase);
            await test.Time(row.ExpiresAt);
            await test.Control("release/delayed/" + phase);
            using var result = await read; await Denied(result, test.Token);
            if (phase == "admitted") await test.Wait("delayed", "checkpointed");
        });

    [Theory]
    [InlineData("before-admission", true)] [InlineData("admitted", false)] [InlineData("first-written", false)]
    public Task Journal_first_withdrawal_drains_actual_detail_writers_and_old_references_never_reopen(string phase, bool completed) =>
        ReferenceTopology.Run(fixture, "withdrawal-" + phase, async test =>
        {
            var row = await test.Discover();
            await test.Control("hold/held/" + phase);
            var read = test.Request("/api/drivers/scoped/detail", "held", reference: row.DetailReference);
            await test.Wait("held", phase);
            var operation = Guid.NewGuid();
            var outcome = await test.Transition(ReferenceActors.Target, DriverLifecycleKind.WithdrawSharing, operation);
            Assert.Equal(completed, outcome.Completed);
            using var replay = await test.Request("/api/drivers/scoped/detail", "replay", reference: row.DetailReference, secondHost: true);
            await Denied(replay, test.Token);
            await test.Control("release/held/" + phase);
            using var response = await read;
            if (phase == "first-written") { Assert.Equal(HttpStatusCode.OK, response.StatusCode); Safe(await response.Content.ReadAsStringAsync(test.Token)); }
            else await Denied(response, test.Token);
            if (phase != "before-admission") await test.Wait("held", "checkpointed");
            Assert.True((await test.Transition(ReferenceActors.Target, DriverLifecycleKind.WithdrawSharing, operation)).Completed);
        });

    [Theory]
    [InlineData(false, DriverLifecycleKind.WithdrawSharing)] [InlineData(false, DriverLifecycleKind.RevokeProof)]
    [InlineData(false, DriverLifecycleKind.Unlink)] [InlineData(true, DriverLifecycleKind.WithdrawPersonal)]
    [InlineData(true, DriverLifecycleKind.RevokeProof)]
    public Task Private_follow_is_immediately_hidden_on_target_or_recipient_relevant_loss(bool recipientLoss, DriverLifecycleKind kind) =>
        ReferenceTopology.Run(fixture, "follow-loss-" + recipientLoss + kind, async test =>
        {
            var row = await test.Discover();
            using var saved = await test.Request("/api/drivers/scoped/follows", "save", reference: row.FollowReference); saved.EnsureSuccessStatusCode();
            await saved.Content.ReadAsStringAsync(test.Token); await test.Wait("save", "checkpointed");
            var operation = Guid.NewGuid();
            await test.Control("hold/" + operation + "/journal-intent-recorded", true);
            var transition = test.Transition(recipientLoss ? ReferenceActors.Recipient : ReferenceActors.Target, kind, operation);
            await test.Wait(operation.ToString(), "journal-intent-recorded", true);
            using var beforeClosure = await test.Request("/api/drivers/scoped/follows", "journal-first");
            await Denied(beforeClosure, test.Token);
            await test.Control("release/" + operation + "/journal-intent-recorded", true);
            Assert.True((await transition).Completed);
            await using var db = test.Db(); var follow = await db.Set<PrivateDriverFollow>().SingleAsync(test.Token);
            Assert.False(follow.Active); Assert.NotNull(follow.OriginalLossAt);
            Assert.Equal(follow.OriginalLossAt!.Value.AddDays(90), follow.ReactivateBefore);
            Assert.Equal(follow.OriginalLossAt.Value.AddDays(97), follow.RemoveBy);
            var original = follow.OriginalLossAt;
            await test.Transition(recipientLoss ? ReferenceActors.Recipient : ReferenceActors.Target, kind, operation);
            db.ChangeTracker.Clear(); Assert.Equal(original, (await db.Set<PrivateDriverFollow>().SingleAsync(test.Token)).OriginalLossAt);
        });

    [Theory]
    [InlineData(-1, true)] [InlineData(0, false)] [InlineData(1, false)]
    public Task Follow_reactivation_ends_at_original_day90_with_actual_requests_and_fresh_proof(int offsetMicroseconds, bool eligible) =>
        ReferenceTopology.Run(fixture, "follow-reactivation-" + offsetMicroseconds, async test =>
        {
            var row = await test.Discover();
            using var saved = await test.Request("/api/drivers/scoped/follows", "save", reference: row.FollowReference); saved.EnsureSuccessStatusCode();
            await saved.Content.ReadAsStringAsync(test.Token); await test.Wait("save", "checkpointed");
            await test.Transition(ReferenceActors.Target, DriverLifecycleKind.WithdrawSharing);
            await using var db = test.Db(); var follow = await db.Set<PrivateDriverFollow>().AsNoTracking().SingleAsync(test.Token);
            var at = follow.OriginalLossAt!.Value.AddDays(90).AddTicks(offsetMicroseconds * 10L);
            var clock = new ReferenceClock(); clock.Set(at); await test.Grant(ReferenceActors.Target, clock);
            await test.Time(at);
            var fresh = await test.Discover("fresh");
            using var renewed = await test.Request("/api/drivers/scoped/follows", "renew", reference: fresh.FollowReference);
            if (eligible)
            {
                renewed.EnsureSuccessStatusCode(); await renewed.Content.ReadAsStringAsync(test.Token); await test.Wait("renew", "checkpointed");
                var listed = await test.Discover("renewed-list", follows: true); Assert.Equal(row.DriverName, listed.DriverName);
            }
            else await Denied(renewed, test.Token);
            var state = await db.Set<PrivateDriverFollow>().AsNoTracking().SingleAsync(test.Token);
            Assert.Equal(eligible, state.Active); Assert.Equal(follow.OriginalLossAt, state.OriginalLossAt);
            Assert.Equal(follow.ReactivateBefore, state.ReactivateBefore); Assert.Equal(follow.RemoveBy, state.RemoveBy);
            using var stale = await test.Request("/api/drivers/scoped/detail", "stale", reference: row.DetailReference);
            await Denied(stale, test.Token);
        });

    [Fact]
    public Task Erased_inactive_follow_allows_a_new_authorized_collection_without_restoring_old_metadata() =>
        ReferenceTopology.Run(fixture, "fresh-follow-after-erasure", async test =>
        {
            var row = await test.Discover();
            using var saved = await test.Request("/api/drivers/scoped/follows", "save", reference: row.FollowReference);
            saved.EnsureSuccessStatusCode(); await saved.Content.ReadAsStringAsync(test.Token); await test.Wait("save", "checkpointed");
            await test.Transition(ReferenceActors.Target, DriverLifecycleKind.WithdrawSharing);
            await using var db = test.Db();
            var original = await db.Set<PrivateDriverFollow>().AsNoTracking().SingleAsync(test.Token);
            var at = original.OriginalLossAt!.Value.AddDays(90);
            var clock = new ReferenceClock(); clock.Set(at); await test.Grant(ReferenceActors.Target, clock);
            await test.Time(at);
            var fresh = await test.Discover("fresh-day90");

            using var restoration = await test.Request("/api/drivers/scoped/follows", "restore-retained", reference: fresh.FollowReference);
            await Denied(restoration, test.Token); await test.Wait("restore-retained", "checkpointed");
            var retained = await db.Set<PrivateDriverFollow>().AsNoTracking().SingleAsync(test.Token);
            Assert.False(retained.Active); Assert.Equal(original.Id, retained.Id); Assert.Equal(original.CreatedAt, retained.CreatedAt);
            Assert.Equal(original.OriginalLossAt, retained.OriginalLossAt);
            Assert.Equal(original.ReactivateBefore, retained.ReactivateBefore); Assert.Equal(original.RemoveBy, retained.RemoveBy);

            await test.Control("cleanup");
            Assert.Empty(await db.Set<PrivateDriverFollow>().AsNoTracking().ToListAsync(test.Token));
            using var stale = await test.Request("/api/drivers/scoped/follows", "old-reference", reference: row.FollowReference);
            await Denied(stale, test.Token);
            Assert.Empty(await db.Set<PrivateDriverFollow>().AsNoTracking().ToListAsync(test.Token));

            using var collected = await test.Request("/api/drivers/scoped/follows", "new-collection", reference: fresh.FollowReference);
            collected.EnsureSuccessStatusCode();
            Assert.True((await collected.Content.ReadFromJsonAsync<ScopedDriverFollowDto>(test.Token))!.Followed);
            await test.Wait("new-collection", "checkpointed");
            var current = await db.Set<PrivateDriverFollow>().AsNoTracking().SingleAsync(test.Token);
            Assert.NotEqual(original.Id, current.Id); Assert.True(current.Active);
            Assert.Equal(DriverAuthorizationPolicy.DurableTime(at), current.CreatedAt); Assert.NotEqual(original.CreatedAt, current.CreatedAt);
            Assert.Equal(original.RecipientGrantId, current.RecipientGrantId); Assert.Equal(original.TargetGrantId, current.TargetGrantId);
            Assert.Null(current.OriginalLossAt); Assert.Null(current.ReactivateBefore); Assert.Null(current.RemoveBy);
            var listed = await test.Discover("new-follow-list", follows: true);
            Assert.Equal(fresh.DriverName, listed.DriverName); Assert.NotEqual(row.FollowReference, listed.FollowReference);
        });

    [Fact]
    public Task Cleanup_physically_removes_inactive_follow_by_day97_and_User_deletion_by_day7() =>
        ReferenceTopology.Run(fixture, "physical-follow-cleanup", async test =>
        {
            var row = await test.Discover();
            using var saved = await test.Request("/api/drivers/scoped/follows", "save", reference: row.FollowReference); saved.EnsureSuccessStatusCode();
            await saved.Content.ReadAsStringAsync(test.Token); await test.Wait("save", "checkpointed");
            await test.Transition(ReferenceActors.Recipient, DriverLifecycleKind.DeleteUser);
            await using var db = test.Db(); var follow = await db.Set<PrivateDriverFollow>().AsNoTracking().SingleAsync(test.Token);
            Assert.Equal(follow.OriginalLossAt!.Value.AddDays(7), follow.RemoveBy);
            await test.Time(follow.RemoveBy!.Value.AddTicks(-10)); await test.Control("cleanup");
            Assert.Single(await db.Set<PrivateDriverFollow>().ToListAsync(test.Token));
            await test.Time(follow.RemoveBy.Value); await test.Control("cleanup");
            Assert.Empty(await db.Set<PrivateDriverFollow>().ToListAsync(test.Token));
            Assert.Empty(await db.Set<ScopedDriverReference>().ToListAsync(test.Token));
            await test.Control("cleanup"); Assert.Empty(await db.Set<PrivateDriverFollow>().ToListAsync(test.Token));
        });

    [Theory]
    [InlineData("/api/users/me/rivals")] [InlineData("/api/users/me/rivals/search?term=Synthetic")]
    [InlineData("/api/users/me/rivals/suggestions")] [InlineData("/api/users/me/compare?rivalCustId=2")]
    [InlineData("/api/subsessions/1/laps?customerId=2")] [InlineData("/api/subsessions/1")]
    [InlineData("/api/series/1/standings?carClassId=1")] [InlineData("/api/series/1/tt-standings?carClassId=1")]
    [InlineData("/api/series/1/qualify-results?carClassId=1&raceWeekIndex=0")]
    [InlineData("/api/series/1/weeks/0/cars/1/percentile?customerId=2")]
    [InlineData("/api/users/me/profile-stats")] [InlineData("/api/users/me/progression")]
    [InlineData("/api/users/me/races")] [InlineData("/api/leaderboards")]
    [InlineData("/api/series/1/weeks/0")]
    public Task Actual_legacy_routes_remain_unavailable_with_raw_ID_or_stale_Follow(string path) =>
        ReferenceTopology.Run(fixture, "legacy-" + DriverReferences.Hash(path), async test =>
        {
            using var response = await test.Request(path, "legacy");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            Safe(await response.Content.ReadAsStringAsync(test.Token));
        });

    [Fact]
    public Task A_name_generation_change_during_discovery_cannot_publish_the_previous_name() =>
        ReferenceTopology.Run(fixture, "name-generation-discovery-race", async test =>
        {
            await test.Control("hold/raced/reference-candidate-selected");
            var read = test.Request("/api/drivers/scoped/discovery", "raced");
            await test.Wait("raced", "reference-candidate-selected");
            await using var db = test.Db();
            var store = new DriverAuthorityStore(db, TimeProvider.System);
            await store.GrantAsync(new(Guid.NewGuid(), ReferenceActors.Scope(ReferenceActors.Target), DateTimeOffset.UtcNow.AddSeconds(-1),
                "controlled375-synthetic-proof", "Replacement Synthetic Driver"),
                new(DriverAuthorizationPolicy.PersonalConsentVersion, DriverAuthorizationPolicy.SharingConsentVersion), test.History, test.Token);
            await test.Control("release/raced/reference-candidate-selected");
            using var result = await read; await Denied(result, test.Token);
            Assert.Empty((await test.History.ReadAsync(test.Token)).Releases);
        });

    [Fact]
    public Task Real_namespace_cannot_issue_or_resolve_synthetic_references() =>
        ReferenceTopology.Run(fixture, "cross-namespace", async test =>
        {
            var row = await test.Discover();
            await using var db = test.Db(DataProvenance.Real);
            var store = new DriverReferenceStore(db, TimeProvider.System, test.History);
            Assert.Null(await store.RecipientAsync(ReferenceActors.Recipient, test.Token));
            Assert.Null(await store.IssueAsync(ReferenceActors.Recipient, ReferenceActors.Scope(ReferenceActors.Target), DriverReferencePurpose.Detail, test.Token));
            Assert.Null(await store.ResolveAsync(ReferenceActors.Recipient, row.DetailReference, DriverReferencePurpose.Detail, test.Token));
        });

    [Fact]
    public Task Applied_migrations_reject_raw_reference_rebinding_long_lifetimes_and_dormancy_clock_reset() =>
        ReferenceTopology.Run(fixture, "migration-fences", async test =>
        {
            var row = await test.Discover();
            await using var db = test.Db();
            var hash = DriverReferences.Hash(row.DetailReference);
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync($"UPDATE iracing.\"ScopedDriverReferences\" SET \"ExpiresAt\"=\"ExpiresAt\"+INTERVAL '1 minute' WHERE \"TokenHash\"={hash}", test.Token));
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync($"UPDATE iracing.\"ScopedDriverReferences\" SET \"Purpose\"=2 WHERE \"TokenHash\"={hash}", test.Token));
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync($"INSERT INTO iracing.\"ScopedDriverReferences\" SELECT {new string('B',64)}, \"RecipientGrantId\", \"RecipientRevision\", \"RecipientProofId\", \"TargetGrantId\", \"TargetRevision\", \"TargetProofId\", \"Purpose\", \"Provenance\", \"CreatedAt\", \"ExpiresAt\"+INTERVAL '1 minute' FROM iracing.\"ScopedDriverReferences\" WHERE \"TokenHash\"={hash}", test.Token));
            using var saved = await test.Request("/api/drivers/scoped/follows", "save", reference: row.FollowReference); saved.EnsureSuccessStatusCode();
            await saved.Content.ReadAsStringAsync(test.Token); await test.Wait("save", "checkpointed");
            await test.Transition(ReferenceActors.Target, DriverLifecycleKind.WithdrawSharing);
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE iracing.\"PrivateDriverFollows\" SET \"OriginalLossAt\"=NULL, \"ReactivateBefore\"=NULL, \"RemoveBy\"=NULL, \"Active\"=TRUE", test.Token));
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE iracing.\"PrivateDriverFollows\" SET \"Active\"=TRUE", test.Token));
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE iracing.\"PrivateDriverFollows\" SET \"RemoveBy\"=\"RemoveBy\"+INTERVAL '1 day'", test.Token));
            var migrations = await db.Database.GetAppliedMigrationsAsync(test.Token);
            Assert.Contains(migrations, m => m.EndsWith("ScopedDriverReferences", StringComparison.Ordinal));
        });

    [Fact]
    public Task Withdraw_and_fresh_share_after_saved_follow_selection_cannot_republish_the_inactive_follow() =>
        ReferenceTopology.Run(fixture, "follow-selection-race", async test =>
        {
            var row = await test.Discover();
            using var saved = await test.Request("/api/drivers/scoped/follows", "save", reference: row.FollowReference); saved.EnsureSuccessStatusCode();
            await saved.Content.ReadAsStringAsync(test.Token); await test.Wait("save", "checkpointed");
            await test.Control("hold/raced/reference-follows-selected");
            var read = test.Request("/api/drivers/scoped/follows", "raced"); await test.Wait("raced", "reference-follows-selected");
            await test.Transition(ReferenceActors.Target, DriverLifecycleKind.WithdrawSharing);
            var clock = new ReferenceClock(); clock.Set(DateTimeOffset.UtcNow.AddSeconds(5));
            await test.Grant(ReferenceActors.Target, clock); await test.Time(clock.GetUtcNow());
            await test.Control("release/raced/reference-follows-selected");
            using var result = await read; await Denied(result, test.Token);
            await using var db = test.Db(); Assert.False((await db.Set<PrivateDriverFollow>().SingleAsync(test.Token)).Active);
        });

    [Theory]
    [InlineData("reference-source-unavailable")] [InlineData("reference-catalog-unavailable")]
    [InlineData("history-unavailable")] [InlineData("journal-read")]
    public Task Missing_source_catalog_independent_history_or_journal_denies_delayed_admitted_output(string fault) =>
        ReferenceTopology.Run(fixture, "reference-fault-" + fault, async test =>
        {
            var row = await test.Discover();
            await test.Control("hold/held/admitted");
            var request = test.Request("/api/drivers/scoped/detail", "held", reference: row.DetailReference); await test.Wait("held", "admitted");
            await test.Control("fault/" + fault); await test.Control("release/held/admitted");
            using var response = await request; await Denied(response, test.Token);
            // Unknown independent status retains conservative accounting and never proves terminal.
            if (fault is not "history-unavailable") await test.Wait("held", "checkpointed");
            else await test.Wait("held", "request-ended");
            await test.Control("clear/" + fault);
            if (fault == "history-unavailable")
            {
                var history = await test.History.ReadAsync(test.Token);
                Assert.False(history.Releases[1].Terminal); Assert.False(history.Releases[1].ProvenUnsent);
                await using var db = test.Db();
                Assert.Null((await db.Set<PublicationRelease>().SingleAsync(r => r.Id == history.Releases[1].Proposal.Id, test.Token)).TerminalAt);
            }
        });

    [Fact]
    public Task A_same_values_replacement_copy_invalidates_original_catalog_and_delayed_response() =>
        ReferenceTopology.Run(fixture, "reference-copy-replacement", async test =>
        {
            var row = await test.Discover(); await test.Control("hold/held/admitted");
            var read = test.Request("/api/drivers/scoped/detail", "held", reference: row.DetailReference); await test.Wait("held", "admitted");
            await using var db = test.Db(); await ReferenceActors.SeedAsync(db, test.Token, initializeGenesis: false);
            await test.Control("release/held/admitted"); using var result = await read; await Denied(result, test.Token);
            await test.Wait("held", "checkpointed");
            using var fresh = await test.Request("/api/drivers/scoped/discovery", "fresh"); await Denied(fresh, test.Token);
        });

    [Theory]
    [InlineData(90, -1, false)] [InlineData(90, 0, true)] [InlineData(90, 1, true)]
    [InlineData(97, -1, true)] [InlineData(97, 0, true)] [InlineData(97, 1, true)]
    public Task Inactive_follow_is_physically_deletion_due_at_day90_and_absent_by_day97(int day, int offsetMicroseconds, bool removed) =>
        ReferenceTopology.Run(fixture, "follow-cleanup-" + day + "-" + offsetMicroseconds, async test =>
        {
            var row = await test.Discover();
            using var saved = await test.Request("/api/drivers/scoped/follows", "save", reference: row.FollowReference); saved.EnsureSuccessStatusCode();
            await saved.Content.ReadAsStringAsync(test.Token); await test.Wait("save", "checkpointed");
            await test.Transition(ReferenceActors.Target, DriverLifecycleKind.WithdrawSharing);
            await using var db = test.Db(); var follow = await db.Set<PrivateDriverFollow>().AsNoTracking().SingleAsync(test.Token);
            await test.Time(follow.OriginalLossAt!.Value.AddDays(day).AddTicks(offsetMicroseconds * 10L));
            using var denied = await test.Request("/api/drivers/scoped/follows", "inactive"); await Denied(denied, test.Token);
            await test.Control("cleanup"); Assert.Equal(removed ? 0 : 1, await db.Set<PrivateDriverFollow>().CountAsync(test.Token));
            await test.Control("cleanup"); Assert.Equal(removed ? 0 : 1, await db.Set<PrivateDriverFollow>().CountAsync(test.Token));
        });

    [Fact]
    public Task Raw_recipient_binding_loss_closes_follow_even_when_personal_consent_column_is_retained() =>
        ReferenceTopology.Run(fixture, "raw-recipient-closure-fence", async test =>
        {
            var row = await test.Discover();
            using var saved = await test.Request("/api/drivers/scoped/follows", "save", reference: row.FollowReference); saved.EnsureSuccessStatusCode();
            await saved.Content.ReadAsStringAsync(test.Token); await test.Wait("save", "checkpointed");
            await using var db = test.Db(); var loss = DriverAuthorizationPolicy.DurableTime(DateTimeOffset.UtcNow);
            await db.Database.ExecuteSqlAsync($"UPDATE iracing.\"DriverAuthorizationGrants\" SET \"BindingActive\"=FALSE, \"PersonalClosedAt\"={loss} WHERE \"UserId\"={ReferenceActors.Recipient}", test.Token);
            var follow = await db.Set<PrivateDriverFollow>().AsNoTracking().SingleAsync(test.Token);
            Assert.False(follow.Active); Assert.Equal(loss, follow.OriginalLossAt);
            using var denied = await test.Request("/api/drivers/scoped/follows", "closed"); await Denied(denied, test.Token);
        });

    [Fact]
    public Task Both_actual_hosts_must_end_their_detail_writers_before_target_withdrawal_completes() =>
        ReferenceTopology.Run(fixture, "reference-two-host-drain", async test =>
        {
            var row = await test.Discover();
            await test.Control("hold/a/first-written"); await test.Control("hold/b/first-written", true);
            using var a = await test.Request("/api/drivers/scoped/detail", "a", reference: row.DetailReference);
            using var b = await test.Request("/api/drivers/scoped/detail", "b", reference: row.DetailReference, secondHost: true);
            await test.Wait("a", "first-written"); await test.Wait("b", "first-written", true);
            var operation = Guid.NewGuid(); Assert.Equal(2, (await test.Transition(ReferenceActors.Target, DriverLifecycleKind.WithdrawSharing, operation)).PendingWriters);
            await test.Control("release/a/first-written"); await a.Content.ReadAsStringAsync(test.Token); await test.Wait("a", "checkpointed");
            Assert.Equal(1, (await test.Transition(ReferenceActors.Target, DriverLifecycleKind.WithdrawSharing, operation)).PendingWriters);
            await test.Control("release/b/first-written", true); await b.Content.ReadAsStringAsync(test.Token); await test.Wait("b", "checkpointed", true);
            Assert.True((await test.Transition(ReferenceActors.Target, DriverLifecycleKind.WithdrawSharing, operation)).Completed);
        });

    [Fact]
    public Task Prepared_result_cannot_execute_under_a_different_authenticated_principal() =>
        ReferenceTopology.Run(fixture, "execution-principal-binding", async test =>
        {
            var row = await test.Discover(); await using var db = test.Db();
            var marker = await db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(m => m.Kind == EvidenceCopyKind.MappedCache, test.Token);
            var purpose = await db.EvidencePurposes.AsNoTracking().SingleAsync(p => p.Id == marker.PurposeId, test.Token);
            var genesis = new ControlledCohortEvidence(marker.Id, marker.Version, purpose.Id, purpose.Generation, purpose.EvidenceVersion, marker.OriginalAcquiredAt, purpose.CreatedAt);
            var history = await test.History.ReadAsync(test.Token);
            var module = new ScopedDriverPublication(new(db, TimeProvider.System, test.History), test.History, Guid.NewGuid(),
                new ReferenceCatalog(db, genesis, new LifecycleGates()), new PublicationReleaseStore(db, TimeProvider.System, test.History, new ReferenceCompositionReview(), history.Epoch));
            var result = await module.ReadAsync(ReferenceActors.Recipient, row.DetailReference, DriverReferencePurpose.Detail, test.Token);
            var services = new ServiceCollection(); services.AddLogging(); services.AddControllers();
            await using var provider = services.BuildServiceProvider();
            var http = new DefaultHttpContext { RequestServices = provider, User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(JwtRegisteredClaimNames.Sub, ReferenceActors.Other.ToString())], "Bearer")) };
            http.Response.Body = new MemoryStream();
            await result.ExecuteResultAsync(new ActionContext { HttpContext = http });
            Assert.Equal(503, http.Response.StatusCode); http.Response.Body.Position=0;
            var body = await new StreamReader(http.Response.Body).ReadToEndAsync(test.Token); Assert.DoesNotContain(row.DriverName, body);
            Assert.Single((await test.History.ReadAsync(test.Token)).Releases);
        });

    [Theory]
    [InlineData("history-reserve-after")] [InlineData("history-dispatch-after")]
    public Task Uncertain_independent_reservation_or_dispatch_keeps_generic_denial_and_conservative_accounting(string fault) =>
        ReferenceTopology.Run(fixture, "reference-uncertain-" + fault, async test =>
        {
            var row = await test.Discover(); await test.Control("fault/" + fault);
            using var response = await test.Request("/api/drivers/scoped/detail", "uncertain", reference: row.DetailReference);
            await Denied(response, test.Token); await test.Control("clear/" + fault);
            var history = await test.History.ReadAsync(test.Token); Assert.Equal(2, history.Releases.Length);
            Assert.False(history.Releases[1].ProvenUnsent);
            Assert.Equal(fault == "history-dispatch-after", history.Releases[1].DispatchStarted);
        });

    internal static async Task<string> Denied(HttpResponseMessage response, CancellationToken ct)
    {
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var body = await response.Content.ReadAsStringAsync(ct); Safe(body);
        Assert.DoesNotContain("Synthetic Reference Driver", body); Assert.DoesNotContain("Reference", body);
        return body;
    }
    private static void Safe(string body)
    {
        foreach (var forbidden in new[] { "customerId", "custId", "targetGrant", "recipientGrant", "privateHistory", "upload", ReferenceActors.Target.ToString(), ReferenceActors.Recipient.ToString() })
            Assert.DoesNotContain(forbidden, body, StringComparison.OrdinalIgnoreCase);
    }
}
