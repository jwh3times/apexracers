using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace ApexRacers.Tests.Helpers;

/// <summary>
/// Forces one insert of a first row to lose the race for it: the first armed <c>INSERT</c> into
/// <paramref name="table"/> is parked before it executes until a competing insert of the same row
/// has executed, so the parked one then meets the unique violation every run (issue #482).
/// </summary>
/// <remarks>
/// <para>
/// Releasing racers through a barrier and hoping two inserts collide is not enough. When the row
/// already exists, every later racer takes the ordinary update path. Final counts come out right
/// whether or not anything collided, so the lost-race fallback can stop running while the test
/// stays green. That happened once already, when pooled fixture connections changed the timing
/// (#391). Holding one insert back until another has gone through makes the collision part of the
/// test's setup instead of a timing accident, independent of connection pooling.
/// </para>
/// <para>
/// The match is on the statement's target, an <c>INSERT INTO</c> the named table, not the table
/// name anywhere in the text: the updates and reads that precede the insert name the same table.
/// </para>
/// </remarks>
/// <param name="table">Unqualified table name the contested insert targets.</param>
/// <param name="constraint">The unique constraint the losing insert is expected to violate.</param>
public sealed class LostInsertRace(string table, string constraint) : DbCommandInterceptor
{
    private readonly Regex insertInto = new(
        $"""INSERT INTO\s+(?:"?\w+"?\.)?"{Regex.Escape(table)}"\s""",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly TaskCompletionSource competitorInserted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock gate = new();
    private DbCommand? heldCommand;
    private bool heldInsertLost;

    /// <summary>
    /// Whether the parked insert went on to fail with a unique violation on the expected
    /// constraint, which is the only way the code under test reaches its lost-race fallback.
    /// </summary>
    public bool HeldInsertLost
    {
        get
        {
            lock (gate)
                return heldInsertLost;
        }
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        await HoldFirstInsertAsync(command, cancellationToken);
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await HoldFirstInsertAsync(command, cancellationToken);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        NoteInserted(command);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        NoteInserted(command);
        return ValueTask.FromResult(result);
    }

    public override Task CommandFailedAsync(
        DbCommand command,
        CommandErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (IsContestedInsert(command)
            && eventData.Exception is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
            } pg
            && pg.ConstraintName == constraint)
        {
            lock (gate)
            {
                if (ReferenceEquals(command, heldCommand))
                    heldInsertLost = true;
            }
        }

        return Task.CompletedTask;
    }

    private async Task HoldFirstInsertAsync(DbCommand command, CancellationToken cancellationToken)
    {
        if (!IsContestedInsert(command))
            return;

        lock (gate)
        {
            if (heldCommand is not null)
                return;
            heldCommand = command;
        }

        try
        {
            await competitorInserted.Task.WaitAsync(RefreshTokenReadBarrier.ArrivalTimeout, cancellationToken);
        }
        catch (TimeoutException ex)
        {
            // Run serially, no racer is left to insert while this one waits. Failing here, with a
            // reason, is the point: the test must not pass without a lost race.
            throw new InvalidOperationException(
                $"No competing insert into {table} executed while the first was held, so no insert lost the race.",
                ex);
        }
    }

    private void NoteInserted(DbCommand command)
    {
        if (!IsContestedInsert(command))
            return;

        lock (gate)
        {
            // The held insert succeeding would mean nothing competed with it; it must not count as
            // its own competitor.
            if (ReferenceEquals(command, heldCommand))
                return;
        }

        competitorInserted.TrySetResult();
    }

    private bool IsContestedInsert(DbCommand command) => insertInto.IsMatch(command.CommandText);
}
