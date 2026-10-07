using System.Collections.Immutable;
using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using ApexRacers.Core;
using ApexRacers.Tests.Lifecycle;
using Npgsql;

namespace ApexRacers.Tests.Publication;

/// <summary>Separate real PostgreSQL synthetic authority with explicit trusted genesis. The
/// committed count/hash survives trailing-row loss; a different genesis is never an empty reset.</summary>
internal sealed class ControlledPublicationHistory(string connectionString, Guid epoch, LifecycleGates gates)
    : IPublicationHistoryAuthority, IDriverEnforcementJournal
{
    private readonly PersistedSyntheticJournal journal = new(connectionString, gates);
    public Task<DriverUserEnforcement> ReadUserAsync(Guid userId, CancellationToken ct = default) => journal.ReadUserAsync(userId, ct);
    public Task<DriverJournalState> ReadAsync(DriverScope scope, CancellationToken ct = default) => journal.ReadAsync(scope, ct);
    public Task<DriverLifecycleIntent> AppendAsync(DriverLifecycleIntent intent, CancellationToken ct = default) => journal.AppendAsync(intent, ct);
    public Task ReconcileAsync(DriverLifecycleIntent intent, long revision, CancellationToken ct = default) => journal.ReconcileAsync(intent, revision, ct);

    public async Task InitializeGenesisAsync(CancellationToken ct)
    {
        await journal.InitializeAsync(ct);
        await using var c = await Open(ct);
        await using var command = new NpgsqlCommand("""
            CREATE TABLE publication_history_meta(id integer PRIMARY KEY CHECK(id=1), epoch uuid NOT NULL,
                count bigint NOT NULL, commitment text NOT NULL);
            CREATE TABLE publication_history(sequence bigint PRIMARY KEY, id uuid UNIQUE NOT NULL, entry jsonb NOT NULL);
            INSERT INTO publication_history_meta VALUES(1,@epoch,0,@empty);
            """, c);
        command.Parameters.AddWithValue("epoch", epoch);
        command.Parameters.AddWithValue("empty", Commitment([]));
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<PublicationHistory> ReadAsync(CancellationToken ct = default)
    {
        if (gates.IsFaulted("history-unavailable")) return new(false, []);
        await using var c = await Open(ct);
        await using var tx = await c.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await Lock(c, tx, ct);
        var result = await Read(c, tx, ct);
        await tx.CommitAsync(ct);
        return gates.IsFaulted("history-read-forced-unavailable") ? result with { Available = false } : result;
    }

    public async Task<PublicationAccounting> ReserveAsync(PublicationProposal proposal, CancellationToken ct = default)
    {
        var entry = await Mutate(entries =>
        {
            if (entries.Any(e => e.Proposal.Id == proposal.Id)) throw new InvalidOperationException("Synthetic duplicate release.");
            var reserved = new PublicationAccounting(entries.Length + 1, proposal, Now);
            return (entries.Add(reserved), reserved);
        }, ct);
        if (gates.IsFaulted("history-reserve-after")) throw new EvidenceCopyUnavailableException();
        return entry;
    }

    public async Task MarkDispatchAsync(Guid releaseId, Guid incarnation, CancellationToken ct = default)
    {
        await Mutate(entries =>
        {
            var index = Index(entries, releaseId, incarnation);
            var old = entries[index];
            if (old.Terminal) throw new EvidenceCopyUnavailableException();
            var next = old with { DispatchStartedAt = old.DispatchStartedAt ?? Now };
            return (entries.SetItem(index, next), next);
        }, ct);
        if (gates.IsFaulted("history-dispatch-after")) throw new EvidenceCopyUnavailableException();
        if (gates.IsFaulted("history-dispatch-read-unavailable")) gates.SetFault("history-read-forced-unavailable");
    }

    public async Task CheckpointAsync(Guid releaseId, Guid incarnation, bool provenUnsent, CancellationToken ct = default)
    {
        if (gates.IsFaulted("history-checkpoint-before")) throw new EvidenceCopyUnavailableException();
        await Mutate(entries =>
        {
            var index = Index(entries, releaseId, incarnation);
            var old = entries[index];
            if (provenUnsent && old.DispatchStarted || old.Terminal && old.ProvenUnsent != provenUnsent)
                throw new EvidenceCopyUnavailableException();
            var next = old with { TerminalAt = old.TerminalAt ?? Now, ProvenUnsent = provenUnsent };
            return (entries.SetItem(index, next), next);
        }, ct);
    }

    private async Task<PublicationAccounting> Mutate(Func<ImmutableArray<PublicationAccounting>,
        (ImmutableArray<PublicationAccounting> Entries, PublicationAccounting Changed)> mutate, CancellationToken ct)
    {
        await using var c = await Open(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await Lock(c, tx, ct);
        var current = await Read(c, tx, ct);
        if (!current.Available) throw new EvidenceCopyUnavailableException();
        var (entries, changed) = mutate(current.Releases);
        await using var write = new NpgsqlCommand("""
            INSERT INTO publication_history(sequence,id,entry) VALUES(@sequence,@id,@entry::jsonb)
            ON CONFLICT(sequence) DO UPDATE SET entry=EXCLUDED.entry;
            UPDATE publication_history_meta SET count=@count,commitment=@hash WHERE id=1 AND epoch=@epoch;
            """, c, tx);
        write.Parameters.AddWithValue("sequence", changed.Sequence);
        write.Parameters.AddWithValue("id", changed.Proposal.Id);
        write.Parameters.AddWithValue("entry", JsonSerializer.Serialize(changed));
        write.Parameters.AddWithValue("count", (long)entries.Length);
        write.Parameters.AddWithValue("hash", Commitment(entries));
        write.Parameters.AddWithValue("epoch", epoch);
        await write.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return changed;
    }

    private async Task<PublicationHistory> Read(NpgsqlConnection c, NpgsqlTransaction tx, CancellationToken ct)
    {
        await using var query = new NpgsqlCommand("SELECT epoch,count,commitment FROM publication_history_meta WHERE id=1", c, tx);
        Guid observedEpoch;
        long count;
        string hash;
        await using (var r = await query.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct)) return new(false, []);
            observedEpoch = r.GetGuid(0); count = r.GetInt64(1); hash = r.GetString(2);
        }
        await using var rows = new NpgsqlCommand("SELECT entry::text FROM publication_history ORDER BY sequence", c, tx);
        var entries = ImmutableArray.CreateBuilder<PublicationAccounting>();
        await using (var r = await rows.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) entries.Add(JsonSerializer.Deserialize<PublicationAccounting>(r.GetString(0))!);
        var values = entries.ToImmutable();
        return new(observedEpoch == epoch && count == values.Length && hash == Commitment(values), values, observedEpoch);
    }

    private static int Index(ImmutableArray<PublicationAccounting> entries, Guid id, Guid incarnation)
    {
        var index = Array.FindIndex(entries.ToArray(), e => e.Proposal.Id == id && e.Proposal.Incarnation == incarnation);
        if (index < 0) throw new EvidenceCopyUnavailableException();
        return index;
    }
    private static string Commitment(ImmutableArray<PublicationAccounting> entries) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(entries)));
    private static DateTimeOffset Now => DriverAuthorizationPolicy.DurableTime(DateTimeOffset.UtcNow);
    private static async Task Lock(NpgsqlConnection c, NpgsqlTransaction tx, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(374)", c, tx);
        await command.ExecuteNonQueryAsync(ct);
    }
    private async Task<NpgsqlConnection> Open(CancellationToken ct)
    {
        var c = new NpgsqlConnection(connectionString);
        await c.OpenAsync(ct);
        return c;
    }
}
