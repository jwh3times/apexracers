using System.Diagnostics.CodeAnalysis;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

/// <summary>
/// Periodically purges long-expired rows from <see cref="ExternalDataCache"/>. The cache itself
/// only evicts lazily (overwrite-on-miss), so without this the table would grow unbounded with
/// abandoned keys (e.g. one-off driver searches). A grace window keeps recently-expired rows in
/// case they are requested again before the next sweep.
/// </summary>
public class ExternalDataCacheCleanupService(IServiceScopeFactory scopeFactory, ILogger<ExternalDataCacheCleanupService> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private static readonly TimeSpan Grace = TimeSpan.FromDays(2);

    /// <summary>Deletes cache rows that expired more than <paramref name="grace"/> ago; returns the count removed.</summary>
    public static async Task<int> PurgeExpiredAsync(
        AppDbContext db, DateTimeOffset now, TimeSpan grace, CancellationToken ct)
    {
        var cutoff = now - grace;
        var stale = await db.ExternalDataCaches
            .Where(c => c.ExpiresAt < cutoff)
            .ToListAsync(ct);
        if (stale.Count == 0)
            return 0;

        db.ExternalDataCaches.RemoveRange(stale);
        await db.SaveChangesAsync(ct);
        return stale.Count;
    }

    [ExcludeFromCodeCoverage] // I/O orchestration shell; the purge logic is tested via PurgeExpiredAsync
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var removed = await PurgeExpiredAsync(db, DateTimeOffset.UtcNow, Grace, stoppingToken);
                if (removed > 0)
                    logger.LogInformation("Purged {Count} expired external-data-cache rows.", removed);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "External-data-cache cleanup failed.");
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
