using System.Data.Common;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ApexRacers.Tests.Helpers;

/// <summary>
/// Commits one extra active refresh token immediately after revocation's first sweep, on its own
/// connection — standing in for a rotation that lands behind that sweep and would otherwise leave a
/// credential nothing ever revisits.
/// </summary>
/// <remarks>
/// Injecting deterministically rather than racing a real rotation is deliberate. The interleaving
/// that produces an escaped successor needs one transaction to commit inside another statement's
/// execution window; driving that with two live rotations makes the test's outcome depend on which
/// one the scheduler runs first, and a concurrency test that passes for the wrong reason half the
/// time is worse than none. This reproduces the *state* that interleaving creates, every run.
/// </remarks>
/// <param name="injections">
/// How many sweeps to inject behind. One reproduces a single rotation slipping in; more than the
/// store's pass limit reproduces the pathological case where credentials keep appearing, which is
/// what proves the retry loop terminates instead of spinning.
/// </param>
public sealed class TokenInjectedBetweenSweeps(
    DbContextOptions<AppDbContext> options,
    Guid userId,
    string rawTokenHash,
    DateTimeOffset expiresAt,
    int injections = 1) : DbCommandInterceptor
{
    private readonly Lock gate = new();
    private int injected;

    /// <summary>Matches the statement revocation uses to stamp a user's active tokens.</summary>
    private static bool IsRevocationSweep(string sql) =>
        sql.Contains("UPDATE", StringComparison.OrdinalIgnoreCase) &&
        sql.Contains("RefreshTokens", StringComparison.Ordinal);

    public override async ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (!ShouldInject(command, out var ordinal))
            return result;

        // A separate context so the insert commits on its own connection, exactly as a concurrent
        // rotation's would — the sweep in flight cannot see it.
        await using var injectedContext = new AppDbContext(options);
        injectedContext.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            // Each injection needs its own hash: the column is uniquely indexed.
            TokenHash = ordinal == 1 ? rawTokenHash : $"{rawTokenHash}-{ordinal}",
            CreatedAt = expiresAt.AddDays(-1),
            ExpiresAt = expiresAt,
        });
        await injectedContext.SaveChangesAsync(cancellationToken);
        return result;
    }

    private bool ShouldInject(DbCommand command, out int ordinal)
    {
        ordinal = 0;
        if (!IsRevocationSweep(command.CommandText))
            return false;

        lock (gate)
        {
            if (injected >= injections)
                return false;

            ordinal = ++injected;
            return true;
        }
    }
}
