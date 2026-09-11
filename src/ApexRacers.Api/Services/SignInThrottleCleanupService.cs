using System.Diagnostics.CodeAnalysis;
using ApexRacers.Core;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

/// <summary>
/// Purges sign-in throttle rows whose windows are long gone.
/// </summary>
/// <remarks>
/// <para>
/// The address table is the one that grows, and it grows on an unauthenticated path: a caller
/// guessing from many machines writes one row per machine per account. That is already far cheaper
/// than an attempt log would be, but it is still attacker-driven, so it needs a sweep rather than
/// waiting for the rows to be looked at again — nothing ever looks up a row for an address that
/// never comes back.
/// </para>
/// <para>
/// The account table holds one row per account and cannot grow with traffic; it is swept anyway so
/// idle accounts do not keep a stale counter forever.
/// </para>
/// <para>
/// A generous grace beyond the window keeps this away from the decision path. Purging a row whose
/// window is merely over is harmless — an expired window already counts as zero — but purging one
/// early would hand back an allowance, so the sweep stays well clear.
/// </para>
/// </remarks>
public class SignInThrottleCleanupService(
    IServiceScopeFactory scopeFactory,
    SignInThrottleOptions options,
    ILogger<SignInThrottleCleanupService> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// <summary>Extra time beyond a window's length before its row is eligible for deletion.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromHours(1);

    /// <summary>
    /// Deletes rows whose last failure is older than their window plus the grace; returns how many
    /// went.
    /// </summary>
    public static async Task<int> PurgeStaleAsync(
        AppDbContext db,
        SignInThrottleOptions options,
        DateTimeOffset now,
        TimeSpan grace,
        CancellationToken ct)
    {
        var addressCutoff = now - options.PerAddressWindow - grace;
        var accountCutoff = now - options.AccountWindow - grace;

        var addresses = await db.SignInAddressFailures
            .Where(f => f.LastFailureAt < addressCutoff)
            .ExecuteDeleteAsync(ct);

        // An account row also paces the owner's email, so it survives while that pacing still
        // matters — deleting it early would let the next burst earn another message immediately.
        var noticeCutoff = now - options.NoticeInterval;
        var accounts = await db.SignInAccountFailures
            .Where(f => f.LastFailureAt < accountCutoff
                        && (f.NoticeSentAt == null || f.NoticeSentAt < noticeCutoff))
            .ExecuteDeleteAsync(ct);

        return addresses + accounts;
    }

    [ExcludeFromCodeCoverage] // I/O orchestration shell; the purge logic is tested via PurgeStaleAsync
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var removed = await PurgeStaleAsync(db, options, DateTimeOffset.UtcNow, Grace, stoppingToken);
                if (removed > 0)
                    logger.LogInformation("Purged {Count} stale sign-in throttle rows.", removed);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Sign-in throttle cleanup failed.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
