using System.Net;
using ApexRacers.Tests.Helpers;
using Xunit;

namespace ApexRacers.Tests.Publication;

[Collection(PostgreSqlCollection.Name)]
public sealed class PublicationDrainRehearsalTests(PostgreSqlFixture fixture)
{
    [Fact]
    public Task Completed_publication_allows_withdrawal_and_closes_new_admission() =>
        PublicationRehearsal.RunAsync(fixture, "healthy-completion", async rehearsal =>
        {
            using var response = await rehearsal.PublishAsync("healthy");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("synthetic-first\nsynthetic-last\n", await response.Content.ReadAsStringAsync());
            await rehearsal.WaitAsync("healthy", "checkpointed");

            var withdrawal = await rehearsal.WithdrawAsync();
            Assert.True(withdrawal.Completed);
            Assert.Equal(0, withdrawal.PendingWriters);
            using var denied = await rehearsal.PublishAsync("after-withdrawal");
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Equal("", await denied.Content.ReadAsStringAsync());
        });

    [Fact]
    public Task Reset_connection_cannot_leave_an_application_writer_running_after_acknowledgment() =>
        PublicationRehearsal.RunAsync(fixture, "connection-reset", async rehearsal =>
        {
            await rehearsal.HoldAsync("reset", "first-written");
            await rehearsal.ResetConnectionAfterFirstChunkAsync("reset");
            await rehearsal.WaitAsync("reset", "client-aborted");
            await rehearsal.WaitAsync("reset", "checkpointed");
            Assert.True((await rehearsal.WithdrawAsync()).Completed);
            await rehearsal.ReleaseAsync("reset", "first-written");
            var events = await rehearsal.ObserveAsync("reset");
            Assert.Contains(events, observation => observation.Phase == "first-written");
            Assert.DoesNotContain(events, observation => observation.Phase == "last-written");
        });

    [Theory]
    [InlineData("before-admission", true)]
    [InlineData("admitted", false)]
    [InlineData("first-written", false)]
    [InlineData("writer-ended", false)]
    public Task Withdrawal_racing_a_held_writer_acknowledges_only_after_terminal_checkpoint(
        string phase, bool completedBeforeRelease) =>
        PublicationRehearsal.RunAsync(fixture, "withdrawal-" + phase, async rehearsal =>
        {
            await rehearsal.HoldAsync("held", phase);
            var publication = rehearsal.PublishAsync("held");
            await rehearsal.WaitAsync("held", phase);

            var pending = await rehearsal.WithdrawAsync();
            Assert.Equal(completedBeforeRelease, pending.Completed);
            Assert.Equal(completedBeforeRelease ? 0 : 1, pending.PendingWriters);
            using var denied = await rehearsal.PublishAsync("new-writer");
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

            await rehearsal.ReleaseAsync("held", phase);
            using var response = await publication;
            if (completedBeforeRelease)
            {
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                Assert.Equal("", await response.Content.ReadAsStringAsync());
            }
            else
            {
                Assert.Equal("synthetic-first\nsynthetic-last\n", await response.Content.ReadAsStringAsync());
                await rehearsal.WaitAsync("held", "checkpointed");
            }
            Assert.True((await rehearsal.WithdrawAsync()).Completed);
        });

    [Fact]
    public Task Client_cancellation_stops_the_held_writer_before_withdrawal_completes() =>
        PublicationRehearsal.RunAsync(fixture, "client-cancellation", async rehearsal =>
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            await rehearsal.HoldAsync("canceled", "admitted");
            var publication = rehearsal.PublishAsync("canceled", cancellation.Token);
            await rehearsal.WaitAsync("canceled", "admitted");
            Assert.False((await rehearsal.WithdrawAsync()).Completed);

            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publication);
            await rehearsal.WaitAsync("canceled", "checkpointed");
            Assert.True((await rehearsal.WithdrawAsync()).Completed);
            await rehearsal.ReleaseAsync("canceled", "admitted");
        });

    [Fact]
    public Task Expired_lease_does_not_prove_a_delayed_writer_is_terminal() =>
        PublicationRehearsal.RunAsync(fixture, "expired-lease", async rehearsal =>
        {
            await rehearsal.HoldAsync("expired", "admitted");
            var publication = rehearsal.PublishAsync("expired");
            await rehearsal.WaitAsync("expired", "admitted");
            await rehearsal.ExpireLeaseAsync("expired");
            Assert.False((await rehearsal.WithdrawAsync()).Completed);
            var rejected = await Assert.ThrowsAsync<HttpRequestException>(() => rehearsal.RetryCheckpointAsync("expired"));
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);

            await rehearsal.ReleaseAsync("expired", "admitted");
            using var response = await publication;
            Assert.Equal("synthetic-first\nsynthetic-last\n", await response.Content.ReadAsStringAsync());
            await rehearsal.WaitAsync("expired", "checkpointed");
            Assert.True((await rehearsal.WithdrawAsync()).Completed);
        });

    [Fact]
    public Task Lost_database_session_releases_its_lock_but_does_not_stop_the_HTTP_writer() =>
        PublicationRehearsal.RunAsync(fixture, "database-session-loss", async rehearsal =>
        {
            await rehearsal.HoldAsync("partitioned", "admitted");
            var publication = rehearsal.PublishAsync("partitioned");
            await rehearsal.WaitAsync("partitioned", "admitted");
            await rehearsal.TerminateWriterSessionAsync("partitioned");
            Assert.True(await rehearsal.CanAcquireFormerWriterLockAsync());
            Assert.False((await rehearsal.WithdrawAsync()).Completed);

            await rehearsal.ReleaseAsync("partitioned", "admitted");
            using var response = await publication;
            Assert.Equal("synthetic-first\nsynthetic-last\n", await response.Content.ReadAsStringAsync());
            await rehearsal.WaitAsync("partitioned", "checkpoint-unknown");
            Assert.False((await rehearsal.WithdrawAsync()).Completed);

            await rehearsal.RetryCheckpointAsync("partitioned");
            Assert.True((await rehearsal.WithdrawAsync()).Completed);
        });

    [Fact]
    public Task Restart_preserves_pending_admission_until_the_old_process_exit_is_confirmed() =>
        PublicationRehearsal.RunAsync(fixture, "process-restart", async rehearsal =>
        {
            await rehearsal.HoldAsync("old-process", "admitted");
            var publication = rehearsal.PublishAsync("old-process");
            await rehearsal.WaitAsync("old-process", "admitted");
            Assert.False((await rehearsal.WithdrawAsync()).Completed);

            await rehearsal.RestartPublisherAsync();
            await Assert.ThrowsAsync<HttpRequestException>(() => publication);
            await rehearsal.ExpireLeaseAsync("old-process");
            Assert.False((await rehearsal.WithdrawAsync()).Completed);
            using var denied = await rehearsal.PublishAsync("new-incarnation");
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

            await rehearsal.RecordConfirmedOldProcessExitAsync();
            Assert.True((await rehearsal.WithdrawAsync()).Completed);
        });

    [Fact]
    public Task Withdrawal_drains_writers_on_both_instances_before_acknowledging() =>
        PublicationRehearsal.RunAsync(fixture, "two-publishing-instances", async rehearsal =>
        {
            await rehearsal.HoldAsync("instance-a", "first-written");
            await rehearsal.HoldAsync("instance-b", "first-written", onCoordinator: true);
            using var a = await rehearsal.PublishAsync("instance-a");
            using var b = await rehearsal.PublishAsync("instance-b", onCoordinator: true);
            await rehearsal.WaitAsync("instance-a", "first-written");
            await rehearsal.WaitAsync("instance-b", "first-written", onCoordinator: true);
            Assert.Equal(2, (await rehearsal.WithdrawAsync()).PendingWriters);

            await rehearsal.ReleaseAsync("instance-a", "first-written");
            Assert.Equal("synthetic-first\nsynthetic-last\n", await a.Content.ReadAsStringAsync());
            await rehearsal.WaitAsync("instance-a", "checkpointed");
            var pending = await rehearsal.WithdrawAsync();
            Assert.False(pending.Completed);
            Assert.Equal(1, pending.PendingWriters);

            await rehearsal.ReleaseAsync("instance-b", "first-written", onCoordinator: true);
            Assert.Equal("synthetic-first\nsynthetic-last\n", await b.Content.ReadAsStringAsync());
            await rehearsal.WaitAsync("instance-b", "checkpointed", onCoordinator: true);
            Assert.True((await rehearsal.WithdrawAsync()).Completed);
        });

    [Fact]
    public Task Database_partition_preserves_pending_work_until_terminal_checkpoint_recovery() =>
        PublicationRehearsal.RunAsync(fixture, "database-link-partition", async rehearsal =>
        {
            await rehearsal.HoldAsync("isolated", "first-written");
            using var publication = await rehearsal.PublishAsync("isolated");
            await rehearsal.WaitAsync("isolated", "first-written");
            rehearsal.PartitionPublisherDatabaseLink();
            Assert.False((await rehearsal.WithdrawAsync()).Completed);

            await rehearsal.ReleaseAsync("isolated", "first-written");
            Assert.Equal("synthetic-first\nsynthetic-last\n", await publication.Content.ReadAsStringAsync());
            await rehearsal.WaitAsync("isolated", "checkpoint-unknown");
            var unavailable = await Assert.ThrowsAsync<HttpRequestException>(() => rehearsal.RetryCheckpointAsync("isolated"));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
            Assert.False((await rehearsal.WithdrawAsync()).Completed);

            rehearsal.RestorePublisherDatabaseLink();
            await rehearsal.RetryCheckpointAsync("isolated");
            Assert.True((await rehearsal.WithdrawAsync()).Completed);
        });
}
