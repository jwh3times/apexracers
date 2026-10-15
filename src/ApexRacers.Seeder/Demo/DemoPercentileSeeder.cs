using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Core.PercentileHistory;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Seeder.Demo;

/// <summary>Fresh classified Race-only Demo snapshots from the actual synthetic Subject Driver's
/// retained Field. User claims and old percentile rows are never inputs. Missing evidence stays absent.</summary>
public sealed class DemoPercentileSeeder(AppDbContext db)
{
    public async Task<int> SeedAsync(CancellationToken ct = default)
    {
        if (db.Provenance != DataProvenance.Demo) throw new EvidenceCopyUnavailableException();
        var users = await db.Users.Select(u => u.Id).ToListAsync(ct);
        if (users.Count == 0) return 0;
        // Include old classified targets, not only surviving Subject laps: disappearing evidence
        // must remove a contribution rather than silently leave the old one behind.
        var catalog = await db.Weeks.Select(w => new { w.Id, SeriesId = w.Season.SeriesId }).ToListAsync(ct);
        var fieldCars = await db.SubsessionResults.Where(r => r.Subsession.WeekId != null)
            .Select(r => r.CarId).Distinct().ToListAsync(ct);
        var oldCars = await db.ClassifiedCarPercentileResults.IgnoreQueryFilters()
            .Where(r => r.OwnerKind == PercentileOwnerKind.DemoSubject && users.Contains(r.UserId))
            .Select(r => r.CarId).Distinct().ToListAsync(ct);
        var cars = fieldCars.Concat(oldCars).Distinct().ToArray();
        if (cars.Length == 0) return 0;
        var refresh = new PercentileRefresh(db, TimeProvider.System, new UnavailableDriverEnforcementJournal());
        var changed = 0;
        foreach (var series in catalog.GroupBy(w => w.SeriesId))
            changed += (await refresh.RefreshSyntheticDemoAsync(users, PercentileSelection.FromRequest(false, null),
                series.Key, series.Select(w => w.Id).ToArray(), cars, ct)).ChangedRows;
        return changed;
    }
}
