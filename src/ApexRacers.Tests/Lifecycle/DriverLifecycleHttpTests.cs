using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Api.Services;
using ApexRacers.Tests.Helpers;
using Xunit;

namespace ApexRacers.Tests.Lifecycle;

[Collection(PostgreSqlCollection.Name)]
public sealed class DriverLifecycleHttpTests(PostgreSqlFixture fixture)
{
    [Fact]
    public Task Matching_synthetic_proof_and_personal_consent_enable_only_original_owners_private_view() =>
        LifecycleTopology.RunAsync(fixture, "personal-positive-negative", ["AUTH-01", "AUTH-02", "AUTH-05"], async test =>
        {
            using var unverified = await test.ReadAsync("unverified");
            await AssertUnavailableAsync(unverified);
            await test.FaultAsync("proof-unavailable");
            using var missingProof = await test.Coordinator.Client.PostAsync("/grant", null, test.CancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, missingProof.StatusCode);
            await test.ClearFaultAsync("proof-unavailable");
            using var noConsent = await test.Coordinator.Client.PostAsync("/grant?consent=none", null, test.CancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, noConsent.StatusCode);
            await test.FaultAsync("proof-conflict");
            using var conflicting = await test.Coordinator.Client.PostAsync("/grant", null, test.CancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, conflicting.StatusCode);
            await test.ClearFaultAsync("proof-conflict");
            await test.GrantAsync();
            using var owner = await test.ReadAsync("owner");
            Assert.Equal(HttpStatusCode.OK, owner.StatusCode);
            Assert.Equal("no-store", owner.Headers.CacheControl?.ToString());
            var body = await owner.Content.ReadAsStringAsync(test.CancellationToken);
            Assert.Contains("Synthetic Owner", body);
            Assert.Contains("\"Audience\":\"owner\"", body);
            AssertSafeBody(body);
            await test.WaitAsync("owner", "checkpointed");
            using var other = await test.ReadAsync("other", actor: "other");
            await AssertUnavailableAsync(other);
            using var sharing = await test.Publisher.Client.GetAsync("/sharing/sharing", test.CancellationToken);
            await AssertUnavailableAsync(sharing);
            var state = await test.SnapshotAsync();
            Assert.Single(state.Grants);
            Assert.Equal(SyntheticLifecycleActors.Owner, state.Grants[0].UserId);
            Assert.Null(state.Grants[0].SharingConsentVersion);
        });

    [Fact]
    public Task Sharing_requires_separate_consent_and_signed_in_recipient_and_withdrawal_preserves_personal_view() =>
        LifecycleTopology.RunAsync(fixture, "sharing-is-separate", ["AUTH-02", "AUTH-03", "HTTP-01"], async test =>
        {
            await test.GrantAsync(sharing: true);
            using var visitor = await test.Publisher.Client.GetAsync("/sharing/visitor?actor=visitor", test.CancellationToken);
            await AssertUnavailableAsync(visitor);
            using var sharing = await test.Publisher.Client.GetAsync("/sharing/recipient", test.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, sharing.StatusCode);
            var body = await sharing.Content.ReadAsStringAsync(test.CancellationToken);
            Assert.Contains("Synthetic Owner", body);
            Assert.Contains("signed-in-sharing", body);
            AssertSafeBody(body);
            await test.WaitAsync("recipient", "checkpointed");
            Assert.True((await test.TransitionAsync(Guid.NewGuid(), DriverLifecycleKind.WithdrawSharing)).Completed);
            using var after = await test.Publisher.Client.GetAsync("/sharing/withdrawn", test.CancellationToken);
            await AssertUnavailableAsync(after);
            using var personal = await test.ReadAsync("still-private");
            Assert.Equal(HttpStatusCode.OK, personal.StatusCode);
            await personal.Content.ReadAsStringAsync(test.CancellationToken);
            await test.WaitAsync("still-private", "checkpointed");
            var snapshot = await test.SnapshotAsync();
            Assert.NotNull(snapshot.Grants.Single().PersonalConsentVersion);
            Assert.Null(snapshot.Grants.Single().SharingConsentVersion);
            Assert.Equal(DriverConsentScope.Sharing, snapshot.Cleanup.Single().Purpose);
        });

    [Fact]
    public Task Controlled_adapter_or_forced_namespace_cannot_open_Real_or_legacy_private_output() =>
        LifecycleTopology.RunAsync(fixture, "real-legacy-unavailable", ["AUTH-01", "AUTH-07", "MIGRATE-01"], async test =>
        {
            await test.GrantAsync();
            await using (var db = test.OpenPrimary())
            {
                // Deliberately permissive pre-spine cache marker, not provider data or new admitted evidence.
                db.ExternalDataCaches.Add(new ExternalDataCache
                {
                    Provenance = DataProvenance.Real, CacheKey = IRacingCacheKeys.Standings(1, 1).Key,
                    Payload = "[{\"driverName\":\"forbidden-legacy-marker\",\"customerId\":123456}]",
                    FetchedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                });
                await db.SaveChangesAsync(test.CancellationToken);
            }
            using var forgedGrant = new HttpRequestMessage(HttpMethod.Post, "/grant-real");
            forgedGrant.Headers.Add("X-ApexRacers-Provenance", "Demo");
            using var result = await test.Coordinator.Client.SendAsync(forgedGrant, test.CancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
            using var request = new HttpRequestMessage(HttpMethod.Get, "/legacy-real");
            request.Headers.Add("X-ApexRacers-Provenance", "Demo");
            request.Headers.Add("X-IRacing-Source", "Demo");
            using var legacy = await test.Publisher.Client.SendAsync(request, test.CancellationToken);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, legacy.StatusCode);
            var body = await legacy.Content.ReadAsStringAsync(test.CancellationToken);
            Assert.DoesNotContain("forbidden-legacy-marker", body);
            AssertSafeBody(body);
            using var actualLegacy = await test.Publisher.Client.GetAsync("/api/series/1/standings?carClassId=1", test.CancellationToken);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, actualLegacy.StatusCode);
            Assert.Equal("no-store", actualLegacy.Headers.CacheControl?.ToString());
            var actualBody = await actualLegacy.Content.ReadAsStringAsync(test.CancellationToken);
            Assert.DoesNotContain("forbidden-legacy-marker", actualBody);
            AssertSafeBody(actualBody);
            using var demo = await test.ReadAsync("demo-still-useful");
            Assert.Equal(HttpStatusCode.OK, demo.StatusCode);
            Assert.Contains("Synthetic Owner", await demo.Content.ReadAsStringAsync(test.CancellationToken));
            await test.WaitAsync("demo-still-useful", "checkpointed");
        });

    [Theory]
    [InlineData("before-admission", true)]
    [InlineData("admitted", false)]
    [InlineData("first-written", false)]
    [InlineData("transport-ended", false)]
    public Task Withdrawal_acknowledges_only_after_actual_protected_executor_terminal_checkpoint(string phase, bool initiallyCompleted) =>
        LifecycleTopology.RunAsync(fixture, "withdrawal-" + phase, ["AUTH-03", "HTTP-01", "HTTP-03"], async test =>
        {
            await test.GrantAsync();
            await test.HoldAsync("held", phase);
            var publication = test.ReadAsync("held");
            await test.WaitAsync("held", phase);
            var operation = Guid.NewGuid();
            var pending = await test.TransitionAsync(operation);
            Assert.Equal(initiallyCompleted, pending.Completed);
            Assert.Equal(initiallyCompleted ? 0 : 1, pending.PendingWriters);
            using var denied = await test.ReadAsync("new-writer");
            await AssertUnavailableAsync(denied);
            if (!initiallyCompleted && phase != "transport-ended")
            {
                var premature = await Assert.ThrowsAsync<HttpRequestException>(() => test.RetryCheckpointAsync("held"));
                Assert.Equal(HttpStatusCode.Conflict, premature.StatusCode);
            }
            await test.ReleaseAsync("held", phase);
            using var response = await publication;
            if (phase is "before-admission" or "admitted")
            {
                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
                Assert.DoesNotContain("Synthetic Owner", await response.Content.ReadAsStringAsync(test.CancellationToken));
            }
            else
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Contains("\"complete\":true", await response.Content.ReadAsStringAsync(test.CancellationToken));
            }
            if (!initiallyCompleted)
                await test.WaitAsync("held", "checkpointed");
            Assert.True((await test.TransitionAsync(operation)).Completed);
            var state = await test.SnapshotAsync();
            Assert.Single(state.Operations);
            Assert.Equal(2, state.Cleanup.Length);
            Assert.All(state.Admissions, a => Assert.NotNull(a.TerminalAt));
        });

    [Theory]
    [InlineData("before-journal", false, false)]
    [InlineData("journal-append-before", false, false)]
    [InlineData("journal-append-after", true, false)]
    [InlineData("journal-intent-recorded", true, false)]
    [InlineData("primary-closed", true, true)]
    [InlineData("drained", true, true)]
    [InlineData("journal-reconcile-before", true, true)]
    [InlineData("journal-reconcile-after", true, true)]
    [InlineData("journal-completed", true, true)]
    public Task Failure_at_each_lifecycle_boundary_restarts_both_hosts_and_replays_same_original_intent(
        string boundary, bool durableIntent, bool appliedPrimary) =>
        LifecycleTopology.RunAsync(fixture, "fault-" + boundary, ["AUTH-06", "AUTH-07", "COPY-01"], async test =>
        {
            await test.GrantAsync(sharing: true);
            var operation = Guid.NewGuid();
            await test.FaultAsync(boundary);
            using var failed = await test.Coordinator.Client.PostAsync($"/transition/{operation}/WithdrawPersonal", null, test.CancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, failed.StatusCode);
            var journal = await test.Journal.ReadAsync(SyntheticLifecycleActors.Scope, test.CancellationToken);
            var firstIntent = journal.PendingIntents.SingleOrDefault();
            // Reconciliation committed before a lost response no longer has a pending intent.
            if (durableIntent && boundary is not ("journal-reconcile-after" or "journal-completed"))
                Assert.NotNull(firstIntent);
            if (!durableIntent)
                Assert.Empty(journal.PendingIntents);
            var state = await test.SnapshotAsync();
            Assert.Equal(appliedPrimary ? 1 : 0, state.Operations.Length);
            Assert.Equal(appliedPrimary ? 2 : 0, state.Cleanup.Length);
            using (var probe = await test.ReadAsync("before-restart"))
            {
                Assert.Equal(durableIntent ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK, probe.StatusCode);
                await probe.Content.ReadAsStringAsync(test.CancellationToken);
                if (!durableIntent) await test.WaitAsync("before-restart", "checkpointed");
            }
            await test.RestartBothAsync();
            var recovered = await test.TransitionAsync(operation);
            Assert.True(recovered.Completed);
            if (firstIntent is not null)
                Assert.Equal(firstIntent.OriginalLossAt, recovered.OriginalLossAt);
            var replay = await test.TransitionAsync(operation);
            Assert.True(replay.Completed);
            Assert.Equal(recovered.OriginalLossAt, replay.OriginalLossAt);
            var final = await test.SnapshotAsync();
            Assert.Single(final.Operations);
            Assert.Equal(2, final.Cleanup.Length);
            Assert.All(final.Cleanup, cleanup => Assert.Equal(recovered.OriginalLossAt, cleanup.OriginalLossAt));
            Assert.Equal(recovered.OriginalLossAt.AddHours(24), final.Cleanup.Single(c => c.Purpose == DriverConsentScope.Sharing).DueAt);
            Assert.Equal(recovered.OriginalLossAt.AddDays(97), final.Cleanup.Single(c => c.Purpose == DriverConsentScope.Personal).DueAt);
            Assert.Empty((await test.Journal.ReadAsync(SyntheticLifecycleActors.Scope, test.CancellationToken)).PendingIntents);
            using var closed = await test.ReadAsync("after-recovery");
            await AssertUnavailableAsync(closed);
        });

    [Fact]
    public Task Journal_unavailability_closes_warm_authorized_output_and_does_not_create_permission() =>
        LifecycleTopology.RunAsync(fixture, "journal-unavailable", ["AUTH-07"], async test =>
        {
            await test.GrantAsync();
            await test.FaultPublisherAsync("journal-read");
            using var denied = await test.ReadAsync("journal-unavailable");
            await AssertUnavailableAsync(denied);
            Assert.Empty((await test.SnapshotAsync()).Admissions);
            await test.ClearPublisherFaultAsync("journal-read");
            using var allowed = await test.ReadAsync("journal-restored");
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
            await allowed.Content.ReadAsStringAsync(test.CancellationToken);
            await test.WaitAsync("journal-restored", "checkpointed");
        });

    [Fact]
    public Task Coordination_partition_and_expired_lease_leave_late_actual_writer_pending_until_ended_checkpoint_recovery() =>
        LifecycleTopology.RunAsync(fixture, "late-writer-partition", ["HTTP-01", "HTTP-02", "AUTH-06"], async test =>
        {
            await test.GrantAsync();
            await test.HoldAsync("late", "first-written");
            using var output = await test.ReadAsync("late");
            await test.WaitAsync("late", "first-written");
            test.PartitionPrimary();
            await test.ExpireLeasesAsync();
            var operation = Guid.NewGuid();
            Assert.False((await test.TransitionAsync(operation)).Completed);
            await test.ReleaseAsync("late", "first-written");
            Assert.Contains("\"complete\":true", await output.Content.ReadAsStringAsync(test.CancellationToken));
            await test.WaitAsync("late", "transport-ended");
            Assert.False((await test.TransitionAsync(operation)).Completed);
            test.RestorePrimary();
            await test.RetryCheckpointAsync("late");
            Assert.True((await test.TransitionAsync(operation)).Completed);
        });

    [Fact]
    public Task Losing_actual_PostgreSQL_checkpoint_session_does_not_acknowledge_missing_terminal_record() =>
        LifecycleTopology.RunAsync(fixture, "checkpoint-session-loss", ["HTTP-02", "AUTH-06"], async test =>
        {
            await test.GrantAsync();
            await test.HoldAsync("session", "checkpoint-command");
            using var response = await test.ReadAsync("session");
            await test.WaitAsync("session", "checkpoint-command");
            var received = await ReadThroughCompleteMarkerAsync(response, test.CancellationToken);
            Assert.Contains("Synthetic Owner", received);
            var operation = Guid.NewGuid();
            Assert.False((await test.TransitionAsync(operation)).Completed);
            await test.TerminateCheckpointSessionAsync("session");
            await test.ReleaseAsync("session", "checkpoint-command");
            Assert.False((await test.TransitionAsync(operation)).Completed);
            await test.RetryCheckpointAsync("session");
            Assert.True((await test.TransitionAsync(operation)).Completed);
        });

    [Fact]
    public Task Restart_and_lease_expiry_cannot_infer_terminal_checkpoint_for_old_incarnation() =>
        LifecycleTopology.RunAsync(fixture, "restart-pending-writer", ["HTTP-02", "AUTH-06"], async test =>
        {
            await test.GrantAsync();
            await test.HoldAsync("old", "first-written");
            using var response = await test.ReadAsync("old");
            await test.WaitAsync("old", "first-written");
            var operation = Guid.NewGuid();
            Assert.False((await test.TransitionAsync(operation)).Completed);
            await test.RestartBothAsync();
            await test.ExpireLeasesAsync();
            var pending = await test.TransitionAsync(operation);
            Assert.False(pending.Completed);
            Assert.Equal(1, pending.PendingWriters);
            using var denied = await test.ReadAsync("replacement");
            await AssertUnavailableAsync(denied);
            var rejected = await Assert.ThrowsAsync<HttpRequestException>(() => test.RetryCheckpointAsync("old"));
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        });

    [Fact]
    public Task Actual_writers_on_both_hosts_must_checkpoint_before_transition_acknowledges() =>
        LifecycleTopology.RunAsync(fixture, "two-writer-drain", ["HTTP-01", "AUTH-06"], async test =>
        {
            await test.GrantAsync();
            await test.HoldAsync("a", "first-written");
            await test.HoldAsync("b", "first-written", onCoordinator: true);
            using var a = await test.ReadAsync("a");
            using var b = await test.ReadAsync("b", onCoordinator: true);
            await test.WaitAsync("a", "first-written");
            await test.WaitAsync("b", "first-written", onCoordinator: true);
            var operation = Guid.NewGuid();
            Assert.Equal(2, (await test.TransitionAsync(operation)).PendingWriters);
            await test.ReleaseAsync("a", "first-written");
            await a.Content.ReadAsStringAsync(test.CancellationToken);
            await test.WaitAsync("a", "checkpointed");
            var pending = await test.TransitionAsync(operation);
            Assert.False(pending.Completed);
            Assert.Equal(1, pending.PendingWriters);
            await test.ReleaseAsync("b", "first-written", onCoordinator: true);
            await b.Content.ReadAsStringAsync(test.CancellationToken);
            await test.WaitAsync("b", "checkpointed", onCoordinator: true);
            Assert.True((await test.TransitionAsync(operation)).Completed);
        });

    [Fact]
    public Task Actual_TCP_reset_unwinds_owned_writer_before_durable_checkpoint() =>
        LifecycleTopology.RunAsync(fixture, "actual-tcp-reset", ["HTTP-02"], async test =>
        {
            await test.GrantAsync();
            await test.HoldAsync("reset", "first-written");
            using (var socket = new TcpClient())
            {
                var address = test.Publisher.Client.BaseAddress!;
                await socket.ConnectAsync(address.Host, address.Port, test.CancellationToken);
                var stream = socket.GetStream();
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"GET /private/reset HTTP/1.1\r\nHost: {address.Authority}\r\n\r\n"), test.CancellationToken);
                var received = new StringBuilder();
                var buffer = new byte[4096];
                while (!received.ToString().Contains("Synthetic Owner", StringComparison.Ordinal))
                {
                    var count = await stream.ReadAsync(buffer, test.CancellationToken);
                    Assert.NotEqual(0, count);
                    received.Append(Encoding.ASCII.GetString(buffer, 0, count));
                    Assert.True(received.Length < 16384);
                }
                Assert.DoesNotContain("\"complete\":true", received.ToString());
                socket.Client.LingerState = new LingerOption(true, 0);
            }
            await test.WaitAsync("reset", "checkpointed");
            Assert.True((await test.TransitionAsync(Guid.NewGuid())).Completed);
            await test.ReleaseAsync("reset", "first-written");
            var events = (await test.Publisher.Client.GetFromJsonAsync<LifecycleEvent[]>("/control/events", test.CancellationToken))!;
            var ended = events.Single(e => e.Id == "reset" && e.Phase == "transport-ended");
            var checkpoint = events.Single(e => e.Id == "reset" && e.Phase == "checkpointed");
            Assert.True(ended.At <= checkpoint.At);
        });

    [Fact]
    public Task Journal_first_intent_blocks_delayed_copy_commit_before_primary_closure_and_retains_existing_copy_work() =>
        LifecycleTopology.RunAsync(fixture, "copy-journal-first-veto", ["AUTH-06", "COPY-01"], async test =>
        {
            await test.GrantAsync();
            using var initial = await test.Publisher.Client.PostAsync("/copy/initial", null, test.CancellationToken);
            initial.EnsureSuccessStatusCode();
            await test.HoldAsync("delayed", "copy-prepared");
            var delayed = test.Publisher.Client.PostAsync("/copy/delayed", null, test.CancellationToken);
            await test.WaitAsync("delayed", "copy-prepared");
            var operation = Guid.NewGuid();
            await test.HoldAsync(operation.ToString(), "journal-intent-recorded", onCoordinator: true);
            var transition = test.TransitionAsync(operation);
            await test.WaitAsync(operation.ToString(), "journal-intent-recorded", onCoordinator: true);
            var beforePrimary = await test.SnapshotAsync();
            Assert.Empty(beforePrimary.Operations);
            Assert.Single(beforePrimary.Copies);
            Assert.Null(beforePrimary.Copies[0].UnavailableAt);
            await test.ReleaseAsync("delayed", "copy-prepared");
            using var refused = await delayed;
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Single((await test.SnapshotAsync()).Copies);
            await test.ReleaseAsync(operation.ToString(), "journal-intent-recorded", onCoordinator: true);
            Assert.True((await transition).Completed);
            var after = await test.SnapshotAsync();
            Assert.NotNull(after.Copies.Single().UnavailableAt);
            Assert.Single(after.Operations);
            Assert.Equal(2, after.Cleanup.Length);
            Assert.All(after.Cleanup, work => Assert.Equal(after.Operations[0].OriginalLossAt, work.OriginalLossAt));
        });

    private static async Task AssertUnavailableAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Synthetic Owner", body);
        AssertSafeBody(body);
    }

    private static async Task<string> ReadThroughCompleteMarkerAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var stream = await response.Content.ReadAsStreamAsync(ct);
        var bytes = new byte[4096];
        var received = new StringBuilder();
        while (!received.ToString().Contains("\"complete\":true", StringComparison.Ordinal))
        {
            var count = await stream.ReadAsync(bytes, ct);
            Assert.NotEqual(0, count);
            received.Append(Encoding.UTF8.GetString(bytes, 0, count));
            Assert.True(received.Length <= 4096);
        }
        return received.ToString();
    }

    private static void AssertSafeBody(string body)
    {
        Assert.DoesNotContain("123456", body);
        Assert.DoesNotContain("customerId", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("upload", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("privateHistory", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(SyntheticLifecycleActors.Owner.ToString(), body);
        Assert.DoesNotContain(SyntheticLifecycleActors.Other.ToString(), body);
    }
}
