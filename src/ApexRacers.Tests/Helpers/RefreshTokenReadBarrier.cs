using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ApexRacers.Tests.Helpers;

/// <summary>
/// Forces the interleaving that makes a refresh-token rotation race observable: it holds every
/// armed token-lookup read open until <paramref name="participants"/> of them have completed, so
/// each racer has decided the token is usable before any of them is allowed to act on it.
/// </summary>
/// <remarks>
/// <para>
/// The gate sits on the <em>read</em>, not on <c>SaveChanges</c>, deliberately. A barrier at
/// <c>SaveChanges</c> only reproduces a race whose window happens to span that call, so it would
/// stop reproducing the moment the fix moved the token's consumption anywhere else — and a
/// concurrency test that silently stops exercising the concurrency is worse than no test. Gating
/// the lookup instead reproduces the interleaving for any implementation that reads the token
/// before consuming it, which every candidate fix does.
/// </para>
/// <para>
/// Arming is explicit because setup work (issuing the tokens under test) runs through the same
/// store and would otherwise trip the gate before the racers reach it.
/// </para>
/// </remarks>
public sealed class RefreshTokenReadBarrier(int participants = 2) : DbCommandInterceptor
{
    private readonly TaskCompletionSource release =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock gate = new();
    private int arrivals;
    private bool armed;

    /// <summary>How long a racer waits before the test is declared hung rather than blocking forever.</summary>
    public static readonly TimeSpan ArrivalTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How many armed lookups the gate actually held. Tests assert this, because a barrier that
    /// silently stops matching would leave them passing while exercising no concurrency at all —
    /// the interleaving would simply never be forced, and the race would go unnoticed again.
    /// </summary>
    public int Arrivals => Volatile.Read(ref arrivals);

    public void Arm()
    {
        lock (gate)
            armed = true;
    }

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        if (!IsArmedTokenLookup(command))
            return result;

        if (Interlocked.Increment(ref arrivals) == participants)
        {
            // The gate has done its job. Disarming keeps the arrival count meaning "racers held",
            // rather than drifting upward on every later lookup the test happens to make.
            lock (gate)
                armed = false;
            release.TrySetResult();
        }

        // A racer that fails before reaching the gate would otherwise hang its partner for the
        // whole run; the timeout turns that into a readable test failure instead.
        await release.Task.WaitAsync(ArrivalTimeout, cancellationToken);
        return result;
    }

    private bool IsArmedTokenLookup(DbCommand command)
    {
        lock (gate)
        {
            if (!armed)
                return false;
        }

        // Match the lookup's *predicate*, not the column anywhere in the statement: every SELECT
        // over this table names TokenHash in its projection too, so a bare substring match also
        // caught unrelated verification reads and inflated the arrival count.
        return command.CommandText.Contains("\"TokenHash\" =", StringComparison.Ordinal);
    }
}
