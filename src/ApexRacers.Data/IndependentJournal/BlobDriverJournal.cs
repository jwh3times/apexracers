using System.Collections.Immutable;
using System.Text.Json;
using ApexRacers.Core;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace ApexRacers.Data.IndependentJournal;

/// <summary>Secret material and trust anchor for the independent journal. <see cref="NameHmac"/>
/// derives blob names; <see cref="PayloadAes"/> encrypts records. <see cref="WriteGenesisWhenEmpty"/>
/// is an operator's one-time provisioning switch, never a recovery path.</summary>
public sealed record IndependentJournalSettings(byte[] NameHmac, byte[] PayloadAes, Guid TrustedEpoch, bool WriteGenesisWhenEmpty = false);

/// <summary>
/// The production independent enforcement journal and publication history, over write-once blob
/// storage that a primary-database restore cannot reach. Every fact is its own immutable blob,
/// created only if absent and never overwritten, appended to or deleted. Names carry keyed hashes
/// rather than identifiers; payloads are encrypted. Reads fail closed: anything missing,
/// unreadable or inconsistent reports unavailable, never an empty, permissive state.
/// </summary>
public sealed class BlobDriverJournal : IDriverEnforcementJournal, IPublicationHistoryAuthority
{
    private const int ReservationAttempts = 16;
    private const string GenesisName = "genesis";

    private readonly BlobContainerClient enforcement;
    private readonly BlobContainerClient history;
    private readonly JournalCodec codec;
    private readonly IndependentJournalSettings settings;
    private readonly TimeProvider clock;
    private volatile bool trusted;

    private sealed record Genesis(int Version, Guid Epoch, string NamingFingerprint, string SealingFingerprint);
    private sealed record Reconciliation(Guid OperationId, long Revision);
    private interface IReleaseEvent
    {
        Guid ReleaseId { get; }
        Guid Incarnation { get; }
    }
    private sealed record Dispatch(Guid ReleaseId, Guid Incarnation, DateTimeOffset At) : IReleaseEvent;
    private sealed record Terminal(Guid ReleaseId, Guid Incarnation, bool ProvenUnsent, DateTimeOffset At) : IReleaseEvent;
    private sealed class JournalUnavailableException : Exception;

    public BlobDriverJournal(BlobContainerClient enforcement, BlobContainerClient history,
        IndependentJournalSettings settings, TimeProvider clock)
    {
        if (settings.TrustedEpoch == Guid.Empty) throw new ArgumentException("A trusted epoch is required.", nameof(settings));
        this.enforcement = enforcement;
        this.history = history;
        this.settings = settings;
        this.clock = clock;
        codec = new JournalCodec(settings.NameHmac, settings.PayloadAes);
    }

    // ---- Enforcement journal ----

    public async Task<DriverUserEnforcement> ReadUserAsync(Guid userId, CancellationToken ct = default)
    {
        try
        {
            await EnsureTrustedAsync(ct);
            var intents = await ReadIntentsAsync(UserPrefix(userId), ct);
            if (intents.Any(i => i.Scope.UserId != userId)) return new(false);
            // A deletion is never reconciled away: it vetoes every historical and future Customer ID.
            return new(true, intents.Where(i => i.Kind == DriverLifecycleKind.DeleteUser).MinBy(i => i.OriginalLossAt));
        }
        catch (Exception e) when (IsFailure(e, ct))
        {
            return new(false);
        }
    }

    public async Task<DriverJournalState> ReadAsync(DriverScope scope, CancellationToken ct = default)
    {
        try
        {
            await EnsureTrustedAsync(ct);
            var intents = await ReadIntentsAsync(ScopePrefix(scope), ct);
            var reconciled = new List<Reconciliation>();
            await foreach (var item in enforcement.GetBlobsAsync(BlobTraits.None, BlobStates.None, ReconciledScopePrefix(scope), ct))
                reconciled.Add(await OpenAsync<Reconciliation>(enforcement, item.Name, ct));
            var operations = intents.Select(i => i.OperationId).ToHashSet();
            // A reconciliation without its intent means records were removed or moved.
            if (intents.Any(i => i.Scope != scope) || reconciled.Any(r => !operations.Contains(r.OperationId)))
                return new DriverJournalState(false, 0, []);
            var closed = reconciled.Select(r => r.OperationId).ToHashSet();
            return new DriverJournalState(true, reconciled.Select(r => r.Revision).DefaultIfEmpty(0).Max(),
                intents.Where(i => !closed.Contains(i.OperationId)).OrderBy(i => i.OriginalLossAt).ToList());
        }
        catch (Exception e) when (IsFailure(e, ct))
        {
            return new DriverJournalState(false, 0, []);
        }
    }

    /// <summary>Claims the operation ID first, so one operation can never land in two scopes,
    /// then records the intent where its scope reads it. The first write fixes the original clock;
    /// a retry may only confirm the same operation.</summary>
    public Task<DriverLifecycleIntent> AppendAsync(DriverLifecycleIntent intent, CancellationToken ct = default) =>
        GuardedWriteAsync(async () =>
        {
            var claimed = await CreateOnceAsync(enforcement, OperationName(intent.OperationId), intent, ct);
            if (!SameOperation(claimed, intent)
                || !SameOperation(await CreateOnceAsync(enforcement, IntentName(claimed), claimed, ct), claimed))
                throw new InvalidOperationException("Driver enforcement operation identity conflict.");
            return claimed;
        }, EnforcementUnavailable, ct);

    public Task ReconcileAsync(DriverLifecycleIntent intent, long revision, CancellationToken ct = default) =>
        GuardedWriteAsync(async () =>
        {
            DriverLifecycleIntent persisted;
            try
            {
                persisted = await OpenAsync<DriverLifecycleIntent>(enforcement, IntentName(intent), ct);
            }
            catch (RequestFailedException missing) when (missing.Status == 404)
            {
                throw new InvalidOperationException("Driver enforcement intent is missing.");
            }
            var expected = new Reconciliation(intent.OperationId, revision);
            if (!SameOperation(persisted, intent)
                || await CreateOnceAsync(enforcement, ReconciledName(intent, revision), expected, ct) != expected)
                throw new InvalidOperationException("Driver enforcement operation identity conflict.");
            return true;
        }, EnforcementUnavailable, ct);

    // ---- Publication history ----

    public async Task<PublicationHistory> ReadAsync(CancellationToken ct = default)
    {
        try
        {
            await EnsureTrustedAsync(ct);
            var releases = await FoldAsync(ct);
            return releases is null ? new(false, []) : new(true, releases.Value, settings.TrustedEpoch);
        }
        catch (Exception e) when (IsFailure(e, ct))
        {
            return new(false, []);
        }
    }

    public Task<PublicationAccounting> ReserveAsync(PublicationProposal proposal, CancellationToken ct = default) =>
        GuardedWriteAsync(async () =>
        {
            for (var attempt = 0; attempt < ReservationAttempts; attempt++)
            {
                var current = await FoldAsync(ct) ?? throw new EvidenceCopyUnavailableException();
                if (current.Any(r => r.Proposal.Id == proposal.Id))
                    throw new InvalidOperationException("Publication release is already reserved.");
                var sequence = current.Length + 1;
                var occupant = await CreateOnceAsync(history, ReleaseName(sequence),
                    new PublicationAccounting(sequence, proposal, Now), ct);
                // Another writer took this slot: re-read and claim the next one. Our own record
                // here means an earlier attempt landed but its response was lost.
                if (occupant.Proposal.Id != proposal.Id) continue;
                if (occupant.Sequence != sequence || JsonSerializer.Serialize(occupant.Proposal) != JsonSerializer.Serialize(proposal))
                    throw new EvidenceCopyUnavailableException();
                return occupant;
            }
            throw new EvidenceCopyUnavailableException();
        }, HistoryUnavailable, ct);

    public Task MarkDispatchAsync(Guid releaseId, Guid incarnation, CancellationToken ct = default) =>
        GuardedWriteAsync(async () =>
        {
            var release = await FindAsync(releaseId, incarnation, ct);
            if (release.Terminal) throw new EvidenceCopyUnavailableException();
            var stored = await CreateOnceAsync(history, ReleaseEventName("dispatched", releaseId), new Dispatch(releaseId, incarnation, Now), ct);
            if (stored.ReleaseId != releaseId || stored.Incarnation != incarnation) throw new EvidenceCopyUnavailableException();
            // Write, then look: a racing proven-unsent checkpoint is seen by this read or sees our record.
            if (await history.GetBlobClient(ReleaseEventName("terminal", releaseId)).ExistsAsync(ct))
                throw new EvidenceCopyUnavailableException();
            return true;
        }, HistoryUnavailable, ct);

    public Task CheckpointAsync(Guid releaseId, Guid incarnation, bool provenUnsent, CancellationToken ct = default) =>
        GuardedWriteAsync(async () =>
        {
            var release = await FindAsync(releaseId, incarnation, ct);
            if (provenUnsent && release.DispatchStarted) throw new EvidenceCopyUnavailableException();
            if (release.Terminal)
                return release.ProvenUnsent == provenUnsent ? true : throw new EvidenceCopyUnavailableException();
            var stored = await CreateOnceAsync(history, ReleaseEventName("terminal", releaseId),
                new Terminal(releaseId, incarnation, provenUnsent, Now), ct);
            if (stored.ReleaseId != releaseId || stored.Incarnation != incarnation || stored.ProvenUnsent != provenUnsent)
                throw new EvidenceCopyUnavailableException();
            if (provenUnsent && await history.GetBlobClient(ReleaseEventName("dispatched", releaseId)).ExistsAsync(ct))
                throw new EvidenceCopyUnavailableException();
            return true;
        }, HistoryUnavailable, ct);

    private async Task<PublicationAccounting> FindAsync(Guid releaseId, Guid incarnation, CancellationToken ct)
    {
        var current = await FoldAsync(ct) ?? throw new EvidenceCopyUnavailableException();
        return current.SingleOrDefault(r => r.Proposal.Id == releaseId && r.Proposal.Incarnation == incarnation)
            ?? throw new EvidenceCopyUnavailableException();
    }

    /// <summary>Rebuilds the accounting from its records, or returns null when they don't add up:
    /// a gap in the sequence, a duplicate release, or a dispatch/terminal record whose reservation
    /// is gone (which also betrays a lost newest reservation that has one). A release with both a
    /// dispatch and a proven-unsent record reads as possibly sent: the conservative outcome, never a
    /// disclosure proof, and never a permanent outage of write-once history.</summary>
    private async Task<ImmutableArray<PublicationAccounting>?> FoldAsync(CancellationToken ct)
    {
        var reservations = new List<PublicationAccounting>();
        await foreach (var item in history.GetBlobsAsync(BlobTraits.None, BlobStates.None, "releases/", ct))
        {
            var record = await OpenAsync<PublicationAccounting>(history, item.Name, ct);
            if (item.Name != ReleaseName(record.Sequence) || record.DispatchStarted || record.Terminal || record.ProvenUnsent) return null;
            reservations.Add(record);
        }
        reservations.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
        if (!reservations.Select(r => r.Sequence).SequenceEqual(Enumerable.Range(1, reservations.Count).Select(i => (long)i))
            || reservations.Select(r => r.Proposal.Id).Distinct().Count() != reservations.Count) return null;
        var byId = reservations.ToDictionary(r => r.Proposal.Id);
        var dispatches = await ReadReleaseEventsAsync<Dispatch>("dispatched", byId, ct);
        var terminals = await ReadReleaseEventsAsync<Terminal>("terminal", byId, ct);
        if (dispatches is null || terminals is null) return null;
        return [.. reservations.Select(r =>
        {
            var dispatch = dispatches.GetValueOrDefault(r.Proposal.Id);
            var terminal = terminals.GetValueOrDefault(r.Proposal.Id);
            return r with { DispatchStartedAt = dispatch?.At, TerminalAt = terminal?.At, ProvenUnsent = terminal is { ProvenUnsent: true } && dispatch is null };
        })];
    }

    /// <summary>Reads one kind of release event, or null if any names a release or incarnation
    /// that has no reservation.</summary>
    private async Task<Dictionary<Guid, T>?> ReadReleaseEventsAsync<T>(string kind,
        IReadOnlyDictionary<Guid, PublicationAccounting> reservations, CancellationToken ct) where T : IReleaseEvent
    {
        var events = new Dictionary<Guid, T>();
        await foreach (var item in history.GetBlobsAsync(BlobTraits.None, BlobStates.None, $"{kind}/", ct))
        {
            var record = await OpenAsync<T>(history, item.Name, ct);
            if (item.Name != ReleaseEventName(kind, record.ReleaseId)
                || !reservations.TryGetValue(record.ReleaseId, out var reservation)
                || reservation.Proposal.Incarnation != record.Incarnation) return null;
            events[record.ReleaseId] = record;
        }
        return events;
    }

    // ---- Trust anchor ----

    private Genesis Expected => new(1, settings.TrustedEpoch, codec.NamingFingerprint, codec.SealingFingerprint);

    /// <summary>Confirms the containers belong to this epoch and these keys. A wrong naming key
    /// would otherwise find no records and read as permissive, so nothing proceeds without this.
    /// Genesis is written only on an explicit provisioning opt-in, and only into two empty
    /// containers: records without a genesis are never re-anchored.</summary>
    private async Task EnsureTrustedAsync(CancellationToken ct)
    {
        if (trusted) return;
        var genesis = await ReadGenesisAsync(ct);
        if (genesis is null && settings.WriteGenesisWhenEmpty && await EmptyAsync(enforcement, ct) && await EmptyAsync(history, ct))
        {
            try
            {
                await history.GetBlobClient(GenesisName).UploadAsync(BinaryData.FromObjectAsJson(Expected), CreateOnly(), ct);
            }
            catch (RequestFailedException occupied) when (occupied.Status is 409 or 412) { }
            genesis = await ReadGenesisAsync(ct);
        }
        if (genesis != Expected) throw new JournalUnavailableException();
        trusted = true;
    }

    private async Task<Genesis?> ReadGenesisAsync(CancellationToken ct)
    {
        try
        {
            return (await history.GetBlobClient(GenesisName).DownloadContentAsync(ct)).Value.Content.ToObjectFromJson<Genesis>();
        }
        catch (RequestFailedException missing) when (missing.Status == 404)
        {
            return null;
        }
    }

    private static async Task<bool> EmptyAsync(BlobContainerClient container, CancellationToken ct)
    {
        await foreach (var _ in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, null, ct)) return false;
        return true;
    }

    // ---- Storage primitives ----

    private DateTimeOffset Now => DriverAuthorizationPolicy.DurableTime(clock.GetUtcNow());

    /// <summary>Anything but a cancellation the caller asked for counts as the store failing.</summary>
    private static bool IsFailure(Exception e, CancellationToken ct) => e is not OperationCanceledException || !ct.IsCancellationRequested;

    private static Exception EnforcementUnavailable() => new InvalidOperationException("Driver enforcement is unavailable.");
    private static Exception HistoryUnavailable() => new EvidenceCopyUnavailableException();

    /// <summary>Runs a write after the trust anchor checks out. A deliberate refusal passes through;
    /// every other failure except cancellation becomes <paramref name="unavailable"/>, so a
    /// journal-first caller never proceeds past an uncertain write.</summary>
    private async Task<T> GuardedWriteAsync<T>(Func<Task<T>> write, Func<Exception> unavailable, CancellationToken ct)
    {
        try
        {
            await EnsureTrustedAsync(ct);
            return await write();
        }
        catch (Exception e) when (e is InvalidOperationException or EvidenceCopyUnavailableException) { throw; }
        catch (Exception e) when (IsFailure(e, ct))
        {
            throw unavailable();
        }
    }

    private static bool SameOperation(DriverLifecycleIntent persisted, DriverLifecycleIntent intent) =>
        persisted.OperationId == intent.OperationId && persisted.GrantId == intent.GrantId
        && persisted.Scope == intent.Scope && persisted.Kind == intent.Kind;

    private static BlobUploadOptions CreateOnly() => new() { Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All } };

    /// <summary>Creates the record only if its name is free, and returns whatever record holds the
    /// name afterwards. A timed-out or retried write therefore resolves to the stored record: the
    /// caller compares it with what it meant to write. The storage service reports an occupied
    /// name as 412 (conditional header) or 409 (BlobAlreadyExists); both mean the same here.</summary>
    private async Task<T> CreateOnceAsync<T>(BlobContainerClient container, string name, T record, CancellationToken ct)
    {
        try
        {
            await container.GetBlobClient(name).UploadAsync(BinaryData.FromBytes(codec.Seal(name, record)), CreateOnly(), ct);
            return record;
        }
        catch (RequestFailedException occupied) when (occupied.Status is 409 or 412)
        {
            return await OpenAsync<T>(container, name, ct);
        }
    }

    private async Task<T> OpenAsync<T>(BlobContainerClient container, string name, CancellationToken ct)
    {
        var content = await container.GetBlobClient(name).DownloadContentAsync(ct);
        return codec.Open<T>(name, content.Value.Content.ToMemory().Span);
    }

    private async Task<List<DriverLifecycleIntent>> ReadIntentsAsync(string prefix, CancellationToken ct)
    {
        var intents = new List<DriverLifecycleIntent>();
        await foreach (var item in enforcement.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, ct))
            intents.Add(await OpenAsync<DriverLifecycleIntent>(enforcement, item.Name, ct));
        return intents;
    }

    // ---- Names ----

    private string UserSegment(Guid userId) => codec.Segment("user", userId.ToString("N"));
    private string ScopeSegment(DriverScope scope) => codec.Segment("scope", $"{scope.CustomerId}:{(int)scope.Provenance}");
    private string OperationSegment(Guid operationId) => codec.Segment("operation", operationId.ToString("N"));
    private string OperationName(Guid operationId) => $"operations/{OperationSegment(operationId)}";
    private string UserPrefix(Guid userId) => $"intents/{UserSegment(userId)}/";
    private string ScopePrefix(DriverScope scope) => $"{UserPrefix(scope.UserId)}{ScopeSegment(scope)}/";
    private string IntentName(DriverLifecycleIntent intent) => $"{ScopePrefix(intent.Scope)}{OperationSegment(intent.OperationId)}";
    private string ReconciledScopePrefix(DriverScope scope) => $"reconciled/{UserSegment(scope.UserId)}/{ScopeSegment(scope)}/";
    private string ReconciledName(DriverLifecycleIntent intent, long revision) =>
        $"{ReconciledScopePrefix(intent.Scope)}{OperationSegment(intent.OperationId)}/{revision:D19}";
    private static string ReleaseName(long sequence) => $"releases/{sequence:D19}";
    private string ReleaseEventName(string kind, Guid releaseId) => $"{kind}/{codec.Segment("release", releaseId.ToString("N"))}";
}
