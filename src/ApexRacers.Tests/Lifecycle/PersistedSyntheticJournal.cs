using System.Text.Json;
using ApexRacers.Core;
using Npgsql;

namespace ApexRacers.Tests.Lifecycle;

/// <summary>Test-only enforcement in a separately persisted database, never a grant authority.</summary>
internal sealed class PersistedSyntheticJournal(string connectionString, LifecycleGates gates) : IDriverEnforcementJournal
{
    public async Task<DriverUserEnforcement> ReadUserAsync(Guid userId, CancellationToken ct = default)
    {
        if (gates.IsFaulted("journal-read")) return new(false);
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            SELECT intent::text FROM lifecycle_journal
            WHERE user_id=@user AND (intent->>'Kind')::integer=5
            ORDER BY (intent->>'OriginalLossAt')::timestamptz LIMIT 1
            """, connection);
        command.Parameters.AddWithValue("user", userId);
        var value = await command.ExecuteScalarAsync(ct);
        return new(true, value is string json ? JsonSerializer.Deserialize<DriverLifecycleIntent>(json) : null);
    }
    public async Task InitializeAsync(CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            CREATE TABLE IF NOT EXISTS lifecycle_journal (
              operation_id uuid PRIMARY KEY, user_id uuid NOT NULL, customer_id integer NOT NULL,
              provenance integer NOT NULL, intent jsonb NOT NULL, reconciled_revision bigint NULL);
            """, connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<DriverJournalState> ReadAsync(DriverScope scope, CancellationToken ct = default)
    {
        if (scope.Provenance != DataProvenance.Demo || gates.IsFaulted("journal-read"))
            return new DriverJournalState(false, 0, []);
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            SELECT intent::text, reconciled_revision FROM lifecycle_journal
             WHERE user_id=@user AND customer_id=@customer AND provenance=@provenance
            """, connection);
        command.Parameters.AddWithValue("user", scope.UserId);
        command.Parameters.AddWithValue("customer", scope.CustomerId);
        command.Parameters.AddWithValue("provenance", (int)scope.Provenance);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var minimum = 0L;
        var pending = new List<DriverLifecycleIntent>();
        while (await reader.ReadAsync(ct))
        {
            if (reader.IsDBNull(1))
                pending.Add(JsonSerializer.Deserialize<DriverLifecycleIntent>(reader.GetString(0))!);
            else
                minimum = Math.Max(minimum, reader.GetInt64(1));
        }
        return new DriverJournalState(true, minimum, pending);
    }

    public async Task<DriverLifecycleIntent> AppendAsync(DriverLifecycleIntent intent, CancellationToken ct = default)
    {
        RequireSynthetic(intent.Scope);
        FailAt("journal-append-before");
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            INSERT INTO lifecycle_journal(operation_id,user_id,customer_id,provenance,intent)
            VALUES(@id,@user,@customer,@provenance,@intent::jsonb)
            ON CONFLICT(operation_id) DO UPDATE SET operation_id=EXCLUDED.operation_id
            RETURNING intent::text
            """, connection);
        command.Parameters.AddWithValue("id", intent.OperationId);
        command.Parameters.AddWithValue("user", intent.Scope.UserId);
        command.Parameters.AddWithValue("customer", intent.Scope.CustomerId);
        command.Parameters.AddWithValue("provenance", (int)intent.Scope.Provenance);
        command.Parameters.AddWithValue("intent", JsonSerializer.Serialize(intent));
        var persisted = JsonSerializer.Deserialize<DriverLifecycleIntent>((string)(await command.ExecuteScalarAsync(ct))!)!;
        if (persisted.GrantId != intent.GrantId || persisted.Scope != intent.Scope || persisted.Kind != intent.Kind)
            throw new InvalidOperationException("Synthetic operation identity conflict.");
        FailAt("journal-append-after");
        return persisted;
    }

    public async Task ReconcileAsync(DriverLifecycleIntent intent, long revision, CancellationToken ct = default)
    {
        RequireSynthetic(intent.Scope);
        FailAt("journal-reconcile-before");
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            UPDATE lifecycle_journal SET reconciled_revision=GREATEST(COALESCE(reconciled_revision,0),@revision)
             WHERE operation_id=@id AND user_id=@user AND customer_id=@customer AND provenance=@provenance
            """, connection);
        command.Parameters.AddWithValue("revision", revision);
        command.Parameters.AddWithValue("id", intent.OperationId);
        command.Parameters.AddWithValue("user", intent.Scope.UserId);
        command.Parameters.AddWithValue("customer", intent.Scope.CustomerId);
        command.Parameters.AddWithValue("provenance", (int)intent.Scope.Provenance);
        if (await command.ExecuteNonQueryAsync(ct) != 1)
            throw new InvalidOperationException("Synthetic intent is missing.");
        FailAt("journal-reconcile-after");
    }

    private void FailAt(string phase)
    {
        if (gates.IsFaulted(phase))
            throw new InvalidOperationException("Injected synthetic journal interruption.");
    }

    private static void RequireSynthetic(DriverScope scope)
    {
        if (scope.Provenance != DataProvenance.Demo)
            throw new InvalidOperationException("Controlled journal cannot authorize Real evidence.");
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database;
        if (database is null || !database.StartsWith("apexracers_lifecycle_journal_", StringComparison.Ordinal))
            throw new InvalidOperationException("Independent synthetic journal database required.");
        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(ct);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
