using Npgsql;

namespace ApexRacers.Tests.Publication;

internal sealed record WithdrawalObservation(bool Completed, int PendingWriters);

/// <summary>Isolated synthetic protocol tables, not an AppDbContext model or a product migration.</summary>
internal sealed class RehearsalStore(string connectionString)
{
    // Deliberately measured as an insufficient signal, never used as terminal-writer proof.
    public const long SessionLockKey = 368;
    public async Task InitializeAsync(CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            CREATE TABLE rehearsal_authority (id integer PRIMARY KEY, closed boolean NOT NULL);
            INSERT INTO rehearsal_authority VALUES (1, false);
            CREATE TABLE rehearsal_admission (
                id text PRIMARY KEY, incarnation uuid NOT NULL, terminal boolean NOT NULL DEFAULT false,
                lease_until timestamptz NOT NULL DEFAULT now() + interval '1 minute');
            """, connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
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

    public static async Task<bool> AdmitAsync(
        NpgsqlConnection connection, string id, Guid incarnation, CancellationToken ct)
    {
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var authority = new NpgsqlCommand(
            "SELECT closed FROM rehearsal_authority WHERE id = 1 FOR UPDATE", connection, transaction);
        if ((bool)(await authority.ExecuteScalarAsync(ct))!)
            return false;

        await using var insert = new NpgsqlCommand(
            "INSERT INTO rehearsal_admission(id, incarnation) VALUES ($1, $2)", connection, transaction);
        insert.Parameters.AddWithValue(id);
        insert.Parameters.AddWithValue(incarnation);
        await insert.ExecuteNonQueryAsync(ct);
        await using var measurementLock = new NpgsqlCommand(
            "SELECT pg_advisory_lock_shared($1)", connection, transaction);
        measurementLock.Parameters.AddWithValue(SessionLockKey);
        await measurementLock.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    public static async Task CheckpointAsync(NpgsqlConnection connection, string id, Guid incarnation, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            "UPDATE rehearsal_admission SET terminal = true WHERE id = $1 AND incarnation = $2", connection);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(incarnation);
        if (await command.ExecuteNonQueryAsync(ct) != 1)
            throw new InvalidOperationException("Checkpoint does not match this writer incarnation.");
    }

    public async Task<WithdrawalObservation> WithdrawAsync(CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var close = new NpgsqlCommand(
            "UPDATE rehearsal_authority SET closed = true WHERE id = 1", connection, transaction);
        await close.ExecuteNonQueryAsync(ct);
        await using var count = new NpgsqlCommand(
            "SELECT count(*)::integer FROM rehearsal_admission WHERE NOT terminal", connection, transaction);
        var pending = (int)(await count.ExecuteScalarAsync(ct))!;
        await transaction.CommitAsync(ct);
        return new WithdrawalObservation(pending == 0, pending);
    }

    public async Task ExpireLeaseAsync(string id, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            UPDATE rehearsal_admission SET lease_until = now() - interval '1 minute'
            WHERE id = $1 RETURNING lease_until < now()
            """, connection);
        command.Parameters.AddWithValue(id);
        if (await command.ExecuteScalarAsync(ct) is not true)
            throw new InvalidOperationException("No expired synthetic admission was established.");
    }

    public async Task TerminateSessionAsync(int backendPid, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT pg_terminate_backend($1)", connection);
        command.Parameters.AddWithValue(backendPid);
        if (await command.ExecuteScalarAsync(ct) is not true)
            throw new InvalidOperationException("Writer database session was not terminated.");
    }

    public async Task<bool> CanAcquireSessionLockAsync(CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock($1)", connection);
        command.Parameters.AddWithValue(SessionLockKey);
        // Pooling is disabled. Disposing this physical connection also releases this measured lock.
        return (bool)(await command.ExecuteScalarAsync(ct))!;
    }

    public async Task CheckpointStoppedIncarnationAsync(Guid incarnation, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            "UPDATE rehearsal_admission SET terminal = true WHERE incarnation = $1", connection);
        command.Parameters.AddWithValue(incarnation);
        if (await command.ExecuteNonQueryAsync(ct) == 0)
            throw new InvalidOperationException("No admissions belong to the confirmed stopped incarnation.");
    }
}
