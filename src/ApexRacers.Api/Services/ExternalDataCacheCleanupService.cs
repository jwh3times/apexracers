using System.Diagnostics.CodeAnalysis;
using ApexRacers.Core;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

/// <summary>
/// Reconciles durable copy withdrawal/expiry and physically erases unclassified legacy payloads.
/// The thirty-minute cadence removes eligible work promptly; maximum deadlines are not grace periods.
/// </summary>
public class ExternalDataCacheCleanupService(IServiceScopeFactory scopeFactory, ILogger<ExternalDataCacheCleanupService> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan Grace = TimeSpan.Zero;

    public static async Task<int> PurgeUnclassifiedAsync(AppDbContext db, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (db.Database.IsNpgsql()) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({370L})", ct);
        var removed = await db.QuarantinedDataCaches.ExecuteDeleteAsync(ct);
        removed += await db.ExternalDataCaches.IgnoreQueryFilters().Where(c => c.EvidenceCopyId == null).ExecuteDeleteAsync(ct);
        removed += await db.SubsessionResults.IgnoreQueryFilters().Where(c => c.EvidenceCopyId == null).ExecuteDeleteAsync(ct);
        removed += await db.Subsessions.IgnoreQueryFilters().Where(c => c.EvidenceCopyId == null).ExecuteDeleteAsync(ct);
        removed += await db.SeasonCarBops.IgnoreQueryFilters().Where(c => c.EvidenceCopyId == null).ExecuteDeleteAsync(ct);
        removed += await db.CarPercentileResults.IgnoreQueryFilters().Where(c => c.EvidenceCopyId == null).ExecuteDeleteAsync(ct);
        removed += await db.Rivals.IgnoreQueryFilters().Where(c => c.EvidenceCopyId == null).ExecuteDeleteAsync(ct);
        removed += await db.AuthorizedDriverNameCopies.IgnoreQueryFilters().Where(c => c.EvidenceCopyId == null).ExecuteDeleteAsync(ct);
        removed += await db.Weeks.Where(w => w.WeatherEvidenceCopyId == null && w.WeatherSummaryJson != null)
            .ExecuteUpdateAsync(u => u.SetProperty(w => w.WeatherSummaryJson, (string?)null), ct);
        removed += await db.Weeks.Where(w => w.DemoWeatherEvidenceCopyId == null && w.DemoWeatherSummaryJson != null)
            .ExecuteUpdateAsync(u => u.SetProperty(w => w.DemoWeatherSummaryJson, (string?)null), ct);
        await tx.CommitAsync(ct);
        return removed;
    }

    /// <summary>Deletes cache rows that expired more than <paramref name="grace"/> ago; returns the count removed.</summary>
    public static async Task<int> PurgeExpiredAsync(
        AppDbContext db, DateTimeOffset now, TimeSpan grace, CancellationToken ct)
    {
        var cutoff = now - grace;
        var stale = await db.ExternalDataCaches.IgnoreQueryFilters()
            .Where(c => c.Provenance == DataProvenance.Real && c.ExpiresAt < cutoff)
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
                // A quarantined or unclassified payload cannot establish a continuing purpose.
                // Erasure does not relabel it or renew its original acquisition/expiry clocks.
                await PurgeUnclassifiedAsync(db, stoppingToken);
                var lifecycle = new EvidenceCopyLifecycle(db, TimeProvider.System);
                await lifecycle.ReconcileAsync(stoppingToken);
                await new DriverReferenceStore(db, TimeProvider.System, scope.ServiceProvider.GetRequiredService<IDriverEnforcementJournal>()).ReconcileAsync(stoppingToken);
                await new DriverAuthorityStore(db, TimeProvider.System).RemoveDueCopiesAsync(stoppingToken);
                await new DriverAuthorityStore(db, TimeProvider.System).RemoveExpiredExplanationsAsync(stoppingToken);
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
                logger.LogError("External-data-cache cleanup failed ({FailureType}).", ex.GetType().Name);
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
