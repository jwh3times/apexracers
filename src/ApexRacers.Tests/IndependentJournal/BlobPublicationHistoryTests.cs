using System.Collections.Immutable;
using System.Text.Json;
using ApexRacers.Core;
using ApexRacers.Data;
using ApexRacers.Data.IndependentJournal;
using ApexRacers.Tests.Helpers;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Storage.Blobs;
using Xunit;
using static ApexRacers.Tests.IndependentJournal.BlobDriverJournalTests;

namespace ApexRacers.Tests.IndependentJournal;

/// <summary>Publication accounting in the independent account: contiguous reservations, original
/// dispatch/terminal clocks, conservative outcomes under races, and fail-closed history reads.</summary>
public sealed class BlobPublicationHistoryTests(AzuriteFixture storage) : IClassFixture<AzuriteFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_fresh_history_is_available_empty_and_carries_the_trusted_epoch()
    {
        var settings = Settings();
        var (history, _) = await NewHistoryAsync(settings);

        var read = await history.ReadAsync(Ct);

        Assert.True(read.Available);
        Assert.Empty(read.Releases);
        Assert.Equal(settings.TrustedEpoch, read.Epoch);
    }

    [Fact]
    public async Task Reservations_take_contiguous_sequences_and_refuse_duplicates()
    {
        var (history, _) = await NewHistoryAsync();
        var first = Proposal();
        var second = Proposal();

        var a = await history.ReserveAsync(first, Ct);
        var b = await history.ReserveAsync(second, Ct);

        Assert.Equal((1, 2), (a.Sequence, b.Sequence));
        Assert.Equal(Json(first), Json(a.Proposal));
        Assert.False(a.DispatchStarted || a.Terminal || a.ProvenUnsent);
        Assert.Equal([1L, 2L], (await history.ReadAsync(Ct)).Releases.Select(r => r.Sequence));
        await Assert.ThrowsAsync<InvalidOperationException>(() => history.ReserveAsync(first, Ct));
    }

    [Fact]
    public async Task Dispatch_and_checkpoint_keep_their_original_clocks()
    {
        var (history, _) = await NewHistoryAsync();
        var proposal = Proposal();
        await history.ReserveAsync(proposal, Ct);

        await history.MarkDispatchAsync(proposal.Id, proposal.Incarnation, Ct);
        var dispatched = Single(await history.ReadAsync(Ct));
        await history.MarkDispatchAsync(proposal.Id, proposal.Incarnation, Ct);
        await history.CheckpointAsync(proposal.Id, proposal.Incarnation, provenUnsent: false, Ct);
        var terminal = Single(await history.ReadAsync(Ct));
        await history.CheckpointAsync(proposal.Id, proposal.Incarnation, provenUnsent: false, Ct);

        Assert.Equal(dispatched.DispatchStartedAt, terminal.DispatchStartedAt);
        Assert.True(terminal.Terminal);
        Assert.False(terminal.ProvenUnsent);
        Assert.Equal(terminal.TerminalAt, Single(await history.ReadAsync(Ct)).TerminalAt);
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() =>
            history.CheckpointAsync(proposal.Id, proposal.Incarnation, provenUnsent: true, Ct));
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() =>
            history.MarkDispatchAsync(proposal.Id, proposal.Incarnation, Ct));
    }

    [Fact]
    public async Task Proven_unsent_is_final_and_closes_dispatch()
    {
        var (history, _) = await NewHistoryAsync();
        var proposal = Proposal();
        await history.ReserveAsync(proposal, Ct);

        await history.CheckpointAsync(proposal.Id, proposal.Incarnation, provenUnsent: true, Ct);

        var release = Single(await history.ReadAsync(Ct));
        Assert.True(release.Terminal && release.ProvenUnsent && !release.DispatchStarted);
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() =>
            history.MarkDispatchAsync(proposal.Id, proposal.Incarnation, Ct));
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() =>
            history.CheckpointAsync(proposal.Id, proposal.Incarnation, provenUnsent: false, Ct));
    }

    [Fact]
    public async Task Writes_for_an_unknown_release_or_incarnation_are_refused()
    {
        var (history, _) = await NewHistoryAsync();
        var proposal = Proposal();
        await history.ReserveAsync(proposal, Ct);

        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => history.MarkDispatchAsync(proposal.Id, Guid.NewGuid(), Ct));
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => history.MarkDispatchAsync(Guid.NewGuid(), proposal.Incarnation, Ct));
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => history.CheckpointAsync(proposal.Id, Guid.NewGuid(), true, Ct));
        Assert.False(Single(await history.ReadAsync(Ct)).DispatchStarted);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_dispatch_racing_a_proven_unsent_checkpoint_reads_as_possibly_sent(bool dispatchWritesLast)
    {
        var settings = Settings();
        var gate = new GatePolicy(m => m.Request.Method == RequestMethod.Put
            && m.Request.Uri.Path.Contains(dispatchWritesLast ? "/dispatched/" : "/terminal/"));
        var (held, containers) = await NewHistoryAsync(settings, gate);
        var free = new BlobDriverJournal(storage.Raw(containers.Enforcement), storage.Raw(containers.History), settings, TimeProvider.System);
        var proposal = Proposal();
        await free.ReserveAsync(proposal, Ct);

        // The held writer has read the history and is stopped just before its write lands.
        var slow = dispatchWritesLast
            ? held.MarkDispatchAsync(proposal.Id, proposal.Incarnation, Ct)
            : held.CheckpointAsync(proposal.Id, proposal.Incarnation, provenUnsent: true, Ct);
        await gate.Arrived.WaitAsync(Ct);
        if (dispatchWritesLast) await free.CheckpointAsync(proposal.Id, proposal.Incarnation, provenUnsent: true, Ct);
        else await free.MarkDispatchAsync(proposal.Id, proposal.Incarnation, Ct);
        gate.Release();

        // Whichever writer lands second sees the other's record and reports failure.
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => slow);
        var release = Single(await free.ReadAsync(Ct));
        Assert.True(release.Terminal && release.DispatchStarted);
        Assert.False(release.ProvenUnsent);
    }

    [Fact]
    public async Task A_lost_reservation_response_resolves_to_the_same_sequence()
    {
        var lose = new LoseResponsePolicy(m => m.Request.Method == RequestMethod.Put && m.Request.Uri.Path.Contains("/releases/"), 1);
        var (history, _) = await NewHistoryAsync(policies: lose);
        var proposal = Proposal();

        var reserved = await history.ReserveAsync(proposal, Ct);

        Assert.Equal(1, lose.Lost);
        Assert.Equal(1, reserved.Sequence);
        Assert.Equal(proposal.Id, Single(await history.ReadAsync(Ct)).Proposal.Id);
    }

    [Fact]
    public async Task Racing_reservations_from_two_adapters_take_distinct_contiguous_sequences()
    {
        const int Writers = 8;
        var settings = Settings();
        var barrier = new BarrierPolicy(m => m.Request.Method == RequestMethod.Put && m.Request.Uri.Path.Contains("/releases/"), Writers);
        var (first, containers) = await NewHistoryAsync(settings, barrier);
        await first.ReadAsync(Ct);
        var second = new BlobDriverJournal(containers.Enforcement, containers.History, settings, TimeProvider.System);
        var proposals = Enumerable.Range(0, Writers).Select(_ => Proposal()).ToArray();

        var reserved = await Task.WhenAll(proposals.Select((p, i) => (i % 2 == 0 ? first : second).ReserveAsync(p, Ct)));

        // All eight first attempts were held and released together, so all but one collided.
        Assert.True(barrier.Arrivals > Writers);
        Assert.True(barrier.Conflicts >= Writers - 1);
        Assert.Equal(Enumerable.Range(1, Writers).Select(i => (long)i), reserved.Select(r => r.Sequence).Order());
        var read = await first.ReadAsync(Ct);
        Assert.True(read.Available);
        Assert.Equal(proposals.Select(p => p.Id).Order(), read.Releases.Select(r => r.Proposal.Id).Order());
    }

    [Fact]
    public async Task A_missing_reservation_makes_the_history_unavailable()
    {
        var (history, containers) = await NewHistoryAsync();
        foreach (var _ in Enumerable.Range(0, 3)) await history.ReserveAsync(Proposal(), Ct);

        await storage.Raw(containers.History).DeleteBlobAsync($"releases/{2:D19}", cancellationToken: Ct);

        Assert.False((await history.ReadAsync(Ct)).Available);
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => history.ReserveAsync(Proposal(), Ct));
    }

    [Fact]
    public async Task A_dispatch_whose_reservation_is_gone_makes_the_history_unavailable()
    {
        // Losing the newest reservation leaves no gap; its surviving dispatch record still betrays it.
        var (history, containers) = await NewHistoryAsync();
        var proposal = Proposal();
        await history.ReserveAsync(proposal, Ct);
        await history.MarkDispatchAsync(proposal.Id, proposal.Incarnation, Ct);

        await storage.Raw(containers.History).DeleteBlobAsync($"releases/{1:D19}", cancellationToken: Ct);

        Assert.False((await history.ReadAsync(Ct)).Available);
    }

    [Fact]
    public async Task A_history_without_genesis_or_with_another_epoch_is_unavailable()
    {
        var settings = Settings();
        var (history, containers) = await NewHistoryAsync(settings);
        await history.ReserveAsync(Proposal(), Ct);

        var other = new BlobDriverJournal(containers.Enforcement, containers.History, settings with { TrustedEpoch = Guid.NewGuid() }, TimeProvider.System);
        Assert.False((await other.ReadAsync(Ct)).Available);
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => other.ReserveAsync(Proposal(), Ct));

        var bare = await storage.CreateContainersAsync(Ct);
        var unanchored = new BlobDriverJournal(bare.Enforcement, bare.History, Settings(writeGenesisWhenEmpty: false), TimeProvider.System);
        Assert.False((await unanchored.ReadAsync(Ct)).Available);
    }

    [Fact]
    public async Task History_writes_only_create_absent_blobs()
    {
        var recording = new RecordingPolicy();
        var (history, _) = await NewHistoryAsync(policies: recording);
        var proposal = Proposal();
        await history.ReserveAsync(proposal, Ct);
        await history.MarkDispatchAsync(proposal.Id, proposal.Incarnation, Ct);
        await history.MarkDispatchAsync(proposal.Id, proposal.Incarnation, Ct);
        await history.CheckpointAsync(proposal.Id, proposal.Incarnation, false, Ct);
        await history.CheckpointAsync(proposal.Id, proposal.Incarnation, false, Ct);

        Assert.True(recording.Requests.Count(r => r.Method == "PUT") >= 5);
        Assert.DoesNotContain(recording.Requests, r => r.Method == "DELETE");
        Assert.All(recording.Requests.Where(r => r.Method == "PUT"), r =>
        {
            Assert.Equal("*", r.IfNoneMatch);
            Assert.Equal("BlockBlob", r.BlobType);
        });
    }

    internal static PublicationProposal Proposal(Guid? recipient = null)
    {
        var scope = new DriverScope(Guid.NewGuid(), Random.Shared.Next(1, int.MaxValue), DataProvenance.Real);
        return new(Guid.NewGuid(), "context", "representation", "dependencies", "catalog", 1, DataProvenance.Real,
            recipient ?? Guid.NewGuid(), PublicationPurpose.SignedIn, Guid.NewGuid(),
            [new PublicationDependency(scope, Guid.NewGuid(), 3, DriverConsentScope.Sharing, "authority")]);
    }

    private static string Json(PublicationProposal proposal) => JsonSerializer.Serialize(proposal);
    private static PublicationAccounting Single(PublicationHistory history)
    {
        Assert.True(history.Available);
        return Assert.Single(history.Releases);
    }

    private async Task<(BlobDriverJournal History, (BlobContainerClient Enforcement, BlobContainerClient History) Containers)> NewHistoryAsync(
        IndependentJournalSettings? settings = null, params HttpPipelinePolicy[] policies)
    {
        var containers = await storage.CreateContainersAsync(Ct, policies);
        return (new BlobDriverJournal(containers.Enforcement, containers.History, settings ?? Settings(), TimeProvider.System), containers);
    }
}
