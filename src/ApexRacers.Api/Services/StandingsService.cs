using ApexRacers.Api.Dtos;
using ApexRacers.Data;
using Aydsko.iRacingData;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

/// <summary>
/// Championship driver standings for a series' active season, fetched on demand through
/// <see cref="CachedIRacingClient"/> (24-hour TTL; the SDK auto-downloads the chunked
/// standings). The season and the set of car classes are resolved from the local
/// catalog; the caller picks a class (defaults to the first). Only the top
/// <see cref="TopN"/> rows are kept and cached.
/// </summary>
public class StandingsService(AppDbContext db, CachedIRacingClient cached)
{
    public const int TopN = 100;

    public async Task<SeasonStandingsDto> GetDriverStandingsAsync(
        int seriesId, int? carClassId, CancellationToken ct)
    {
        var season = await db.Seasons
            .Where(s => s.SeriesId == seriesId && s.Active)
            .OrderByDescending(s => s.Year)
            .ThenByDescending(s => s.Quarter)
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException($"No active season for series {seriesId}.");

        var seriesName = await db.Series
            .Where(s => s.Id == seriesId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        var classes = await db.SeasonCarClasses
            .Where(sc => sc.SeasonId == season.Id)
            .Join(db.CarClasses, sc => sc.CarClassId, cc => cc.Id,
                (sc, cc) => new CarClassOptionDto(cc.Id, cc.Name))
            .OrderBy(c => c.CarClassName)
            .ToListAsync(ct);

        var selectedClassId = carClassId ?? (classes.Count > 0 ? classes[0].CarClassId : (int?)null);
        if (selectedClassId is null)
            throw new KeyNotFoundException($"No car classes available for series {seriesId}.");

        var className = classes.FirstOrDefault(c => c.CarClassId == selectedClassId)?.CarClassName
            ?? string.Empty;

        var standings = await cached.GetOrFetchAsync<IReadOnlyList<SeasonStandingDto>>(
            $"standings:{season.Id}:{selectedClassId}", TimeSpan.FromHours(24),
            async c =>
            {
                var rows = (await c.GetSeasonDriverStandingsAsync(
                    season.Id, selectedClassId.Value, null, null, ct)).Data.Item2;
                return (rows ?? [])
                    .OrderBy(s => s.Rank)
                    .Take(TopN)
                    .Select(s => new SeasonStandingDto(
                        s.Rank, s.CustomerId, s.DisplayName, s.Division,
                        s.Starts, s.Wins, s.Top5, s.Poles, s.Points,
                        (double)s.AverageFinishPosition, s.Incidents))
                    .ToList();
            },
            ct);

        return new SeasonStandingsDto(
            seriesId, seriesName, selectedClassId.Value, className, classes, standings);
    }
}
