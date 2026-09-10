using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ApexRacers.Tests.Helpers;

/// <summary>
/// Holds the <em>first</em> armed token lookup open until the test releases it, letting every later
/// lookup through. Where <see cref="RefreshTokenReadBarrier"/> makes two racers collide, this pins
/// one racer mid-flight so a second operation can be run to completion around it — which is what
/// turns "reuse revocation overlapped a rotation" from a timing accident into a stated ordering.
/// </summary>
/// <param name="matches">
/// Which statement to park on. Defaults to the rotation's credential lookup; the active-token sweep
/// that reuse revocation runs is the other read worth pinning, and it is a different query.
/// </param>
public sealed class RefreshTokenReadHold(Func<string, bool>? matches = null) : DbCommandInterceptor
{
    /// <summary>Matches the credential lookup's predicate — not the column in a projection.</summary>
    public static readonly Func<string, bool> CredentialLookup =
        sql => sql.Contains("\"TokenHash\" =", StringComparison.Ordinal);

    /// <summary>Matches the sweep that reads a user's still-active tokens.</summary>
    public static readonly Func<string, bool> ActiveTokenSweep =
        sql => sql.Contains("\"RevokedAt\" IS NULL", StringComparison.Ordinal);

    private readonly Func<string, bool> matcher = matches ?? CredentialLookup;

    private readonly TaskCompletionSource arrived =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource release =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock gate = new();
    private bool armed;
    private bool held;

    public void Arm()
    {
        lock (gate)
            armed = true;
    }

    /// <summary>Completes once a lookup is parked at the gate.</summary>
    public Task ArrivedAsync() => arrived.Task.WaitAsync(RefreshTokenReadBarrier.ArrivalTimeout);

    public void Release() => release.TrySetResult();

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        if (!ShouldHold(command))
            return result;

        arrived.TrySetResult();
        await release.Task.WaitAsync(RefreshTokenReadBarrier.ArrivalTimeout, cancellationToken);
        return result;
    }

    private bool ShouldHold(DbCommand command)
    {
        if (!matcher(command.CommandText))
            return false;

        lock (gate)
        {
            if (!armed || held)
                return false;

            held = true;
            return true;
        }
    }
}
