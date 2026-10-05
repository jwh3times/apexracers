using ApexRacers.Core;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Data;

/// <summary>Real acquisition cannot start/resume until explicit Demo copies are torn down.</summary>
public static class RealAcquisitionGuard
{
    public static async Task EnsureDemoTeardownAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.FeatureFlags.AnyAsync(f => f.Key == "iracing-demo" && f.IsEnabled, ct)
            || await db.ExternalDataCaches.IgnoreQueryFilters().AnyAsync(c => c.Provenance == DataProvenance.Demo, ct)
            || await db.Subsessions.IgnoreQueryFilters().AnyAsync(s => s.Provenance == DataProvenance.Demo, ct)
            || await db.SubsessionResults.IgnoreQueryFilters().AnyAsync(s => s.Provenance == DataProvenance.Demo, ct)
            || await db.CarPercentileResults.IgnoreQueryFilters().AnyAsync(s => s.Provenance == DataProvenance.Demo, ct)
            || await db.Rivals.IgnoreQueryFilters().AnyAsync(s => s.Provenance == DataProvenance.Demo, ct)
            || await db.SeasonCarBops.IgnoreQueryFilters().AnyAsync(s => s.Provenance == DataProvenance.Demo, ct)
            || await db.AuthorizedDriverNameCopies.IgnoreQueryFilters().AnyAsync(s => s.Provenance == DataProvenance.Demo, ct)
            || await db.Weeks.AnyAsync(w => w.DemoWeatherSummaryJson != null, ct))
            throw new DemoTeardownRequiredException();
    }
}

public sealed class DemoTeardownRequiredException()
    : Exception("Real acquisition requires completed Demo teardown.");
