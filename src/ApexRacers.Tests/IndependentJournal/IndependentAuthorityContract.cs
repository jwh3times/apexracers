using System.Collections.Immutable;
using ApexRacers.Core;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using ApexRacers.Tests.Lifecycle;
using ApexRacers.Tests.Publication;
using Npgsql;
using Xunit;

namespace ApexRacers.Tests.IndependentJournal;

/// <summary>
/// One behavioral contract for the independent authorities, run against both the controlled
/// PostgreSQL fixture the lifecycle and publication suites were built on and the production blob
/// adapter. A scenario that passes for one and fails for the other is semantic drift.
/// Demo provenance throughout: the controlled fixture refuses Real scopes by design.
/// </summary>
public abstract class IndependentAuthorityContract
{
    private static readonly DateTimeOffset Loss = DriverAuthorizationPolicy.DurableTime(new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));
    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A fresh, genesis-anchored authority implementing both interfaces.</summary>
    protected abstract Task<IAuthority> CreateAsync();

    public interface IAuthority : IDriverEnforcementJournal, IPublicationHistoryAuthority;

    [Fact]
    public async Task Journal_first_intent_is_idempotent_and_keeps_its_original_clock()
    {
        var authority = await CreateAsync();
        var scope = Scope();
        var intent = Intent(scope, DriverLifecycleKind.WithdrawPersonal);

        await authority.AppendAsync(intent, Ct);
        var replay = await authority.AppendAsync(intent with { OriginalLossAt = Loss.AddHours(2) }, Ct);

        Assert.Equal(intent.OriginalLossAt, replay.OriginalLossAt);
        var state = await authority.ReadCurrentAsync(scope, Ct);
        Assert.True(state.Available);
        Assert.Equal([intent.OperationId], state.PendingIntents.Select(i => i.OperationId));
        Assert.False(state.Allows(10, DriverConsentScope.Personal));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            authority.AppendAsync(intent with { Kind = DriverLifecycleKind.Unlink }, Ct));
    }

    [Fact]
    public async Task Withdrawing_sharing_vetoes_sharing_only()
    {
        var authority = await CreateAsync();
        var scope = Scope();
        await authority.AppendAsync(Intent(scope, DriverLifecycleKind.WithdrawSharing), Ct);

        var state = await authority.ReadCurrentAsync(scope, Ct);

        Assert.False(state.Allows(0, DriverConsentScope.Sharing));
        Assert.True(state.Allows(0, DriverConsentScope.Personal));
    }

    [Fact]
    public async Task Reconciliation_clears_pending_intents_and_raises_the_minimum_revision()
    {
        var authority = await CreateAsync();
        var scope = Scope();
        var first = Intent(scope, DriverLifecycleKind.Unlink);
        var second = Intent(scope, DriverLifecycleKind.RevokeProof);
        await authority.AppendAsync(first, Ct);
        await authority.AppendAsync(second, Ct);

        await authority.ReconcileAsync(first, 4, Ct);
        await authority.ReconcileAsync(second, 7, Ct);

        var state = await authority.ReadCurrentAsync(scope, Ct);
        Assert.Empty(state.PendingIntents);
        Assert.Equal(7, state.MinimumRevision);
        Assert.False(state.Allows(6, DriverConsentScope.Personal));
        Assert.True(state.Allows(7, DriverConsentScope.Personal));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            authority.ReconcileAsync(Intent(scope, DriverLifecycleKind.Unlink), 9, Ct));
    }

    [Fact]
    public async Task A_deletion_is_user_wide_permanent_and_earliest_first()
    {
        var authority = await CreateAsync();
        var scope = Scope();
        var later = Intent(scope, DriverLifecycleKind.DeleteUser, Loss.AddHours(1));
        var earlier = Intent(scope with { CustomerId = scope.CustomerId + 1 }, DriverLifecycleKind.DeleteUser, Loss);
        await authority.AppendAsync(later, Ct);
        await authority.AppendAsync(earlier, Ct);
        await authority.ReconcileAsync(earlier, 3, Ct);

        var user = await authority.ReadUserAsync(scope.UserId, Ct);

        Assert.True(user.Available);
        Assert.Equal(earlier.OperationId, user.Deletion?.OperationId);
        var future = await authority.ReadCurrentAsync(scope with { CustomerId = scope.CustomerId + 50 }, Ct);
        Assert.False(future.Allows(100, DriverConsentScope.Personal));
        Assert.Null((await authority.ReadUserAsync(Guid.NewGuid(), Ct)).Deletion);
    }

    [Fact]
    public async Task Reservations_are_contiguous_and_unique()
    {
        var authority = await CreateAsync();
        var first = Proposal();

        var a = await authority.ReserveAsync(first, Ct);
        var b = await authority.ReserveAsync(Proposal(), Ct);

        Assert.Equal((1L, 2L), (a.Sequence, b.Sequence));
        Assert.False(a.DispatchStarted || a.Terminal || a.ProvenUnsent);
        var history = await authority.ReadAsync(Ct);
        Assert.True(history.Available);
        Assert.Equal([1L, 2L], history.Releases.Select(r => r.Sequence));
        await Assert.ThrowsAsync<InvalidOperationException>(() => authority.ReserveAsync(first, Ct));
    }

    [Fact]
    public async Task Dispatch_then_checkpoint_keeps_the_first_clocks_and_refuses_contradictions()
    {
        var authority = await CreateAsync();
        var proposal = Proposal();
        await authority.ReserveAsync(proposal, Ct);

        await authority.MarkDispatchAsync(proposal.Id, proposal.Incarnation, Ct);
        var started = Release(await authority.ReadAsync(Ct)).DispatchStartedAt;
        await authority.MarkDispatchAsync(proposal.Id, proposal.Incarnation, Ct);
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() =>
            authority.CheckpointAsync(proposal.Id, proposal.Incarnation, provenUnsent: true, Ct));
        await authority.CheckpointAsync(proposal.Id, proposal.Incarnation, provenUnsent: false, Ct);

        var release = Release(await authority.ReadAsync(Ct));
        Assert.Equal(started, release.DispatchStartedAt);
        Assert.True(release.Terminal && !release.ProvenUnsent);
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() =>
            authority.MarkDispatchAsync(proposal.Id, proposal.Incarnation, Ct));
    }

    [Fact]
    public async Task Proven_unsent_closes_the_release_without_dispatch()
    {
        var authority = await CreateAsync();
        var proposal = Proposal();
        await authority.ReserveAsync(proposal, Ct);

        await authority.CheckpointAsync(proposal.Id, proposal.Incarnation, provenUnsent: true, Ct);

        var release = Release(await authority.ReadAsync(Ct));
        Assert.True(release.Terminal && release.ProvenUnsent && !release.DispatchStarted);
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() =>
            authority.MarkDispatchAsync(proposal.Id, proposal.Incarnation, Ct));
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() =>
            authority.MarkDispatchAsync(proposal.Id, Guid.NewGuid(), Ct));
    }

    private static PublicationAccounting Release(PublicationHistory history)
    {
        Assert.True(history.Available);
        return Assert.Single(history.Releases);
    }

    private static DriverScope Scope() => new(Guid.NewGuid(), Random.Shared.Next(1, 1_000_000), DataProvenance.Demo);

    private static DriverLifecycleIntent Intent(DriverScope scope, DriverLifecycleKind kind, DateTimeOffset? lossAt = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), scope, kind, lossAt ?? Loss);

    private static PublicationProposal Proposal() =>
        new(Guid.NewGuid(), "context", "representation", "dependencies", "catalog", 1, DataProvenance.Demo, Guid.NewGuid(),
            PublicationPurpose.SignedIn, Guid.NewGuid(),
            [new PublicationDependency(Scope(), Guid.NewGuid(), 2, DriverConsentScope.Sharing, "authority")]);
}

/// <summary>The contract against the controlled PostgreSQL fixture: the reference semantics.</summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class ControlledFixtureContractTests(PostgreSqlFixture postgres) : IndependentAuthorityContract
{
    protected override async Task<IAuthority> CreateAsync()
    {
        var name = "apexracers_ledger_history_" + Guid.NewGuid().ToString("N");
        await using (var connection = new NpgsqlConnection(postgres.Container.GetConnectionString()))
        {
            await connection.OpenAsync(Ct);
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await command.ExecuteNonQueryAsync(Ct);
        }
        var history = new ControlledPublicationHistory(
            new NpgsqlConnectionStringBuilder(postgres.Container.GetConnectionString()) { Database = name }.ConnectionString,
            Guid.NewGuid(), new LifecycleGates());
        await history.InitializeGenesisAsync(Ct);
        return new Both(history, history);
    }

    private sealed class Both(IDriverEnforcementJournal journal, IPublicationHistoryAuthority history) : IAuthority
    {
        public Task<DriverUserEnforcement> ReadUserAsync(Guid userId, CancellationToken ct = default) => journal.ReadUserAsync(userId, ct);
        public Task<DriverJournalState> ReadAsync(DriverScope scope, CancellationToken ct = default) => journal.ReadAsync(scope, ct);
        public Task<DriverLifecycleIntent> AppendAsync(DriverLifecycleIntent intent, CancellationToken ct = default) => journal.AppendAsync(intent, ct);
        public Task ReconcileAsync(DriverLifecycleIntent intent, long revision, CancellationToken ct = default) => journal.ReconcileAsync(intent, revision, ct);
        public Task<PublicationHistory> ReadAsync(CancellationToken ct = default) => history.ReadAsync(ct);
        public Task<PublicationAccounting> ReserveAsync(PublicationProposal proposal, CancellationToken ct = default) => history.ReserveAsync(proposal, ct);
        public Task MarkDispatchAsync(Guid releaseId, Guid incarnation, CancellationToken ct = default) => history.MarkDispatchAsync(releaseId, incarnation, ct);
        public Task CheckpointAsync(Guid releaseId, Guid incarnation, bool provenUnsent, CancellationToken ct = default) =>
            history.CheckpointAsync(releaseId, incarnation, provenUnsent, ct);
    }

    internal static IAuthority Wrap(IDriverEnforcementJournal journal, IPublicationHistoryAuthority history) => new Both(journal, history);
}

/// <summary>The same contract against the production blob adapter.</summary>
public sealed class BlobAdapterContractTests(AzuriteFixture storage) : IndependentAuthorityContract, IClassFixture<AzuriteFixture>
{
    protected override async Task<IAuthority> CreateAsync()
    {
        var containers = await storage.CreateContainersAsync(Ct);
        var adapter = new BlobDriverJournal(containers.Enforcement, containers.History, BlobDriverJournalTests.Settings(), TimeProvider.System);
        return ControlledFixtureContractTests.Wrap(adapter, adapter);
    }
}
