using ApexRacers.Api.Dtos;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

/// <summary>
/// A per-week strategy briefing for a series: track + pit context, a wet-weather risk read from
/// the forecast, and per-car Balance of Performance with its week-over-week shift plus fuel/tire
/// notes. When the caller is iRacing-linked the briefing is personalized with their competitiveness
/// per car (percentile, projected lap, and an "optimal" ranking) from <see cref="CarRecommendationService"/>.
///
/// Public read; the personal overlay is populated only when a linked user id is passed. Composes
/// the bulk-ingested BoP/weather (2.1) and the existing recommendation engine — the pure heuristics
/// live in <see cref="StrategyAnalysis"/>.
/// </summary>
public class StrategyService(AppDbContext db, CarRecommendationService recommendations, MemberContext member)
{
    public async Task<WeekStrategyDto> GetStrategyAsync(
        int seriesId, int weekNumber, Guid? userId, CancellationToken ct)
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

        var week = await db.Weeks
            .Where(w => w.SeasonId == season.Id && w.WeekNumber == weekNumber)
            .Select(w => new
            {
                w.WeekNumber,
                w.TrackId,
                TrackName = w.Track.Name,
                w.Track.ConfigName,
                LengthMiles = w.Track.TrackConfigLength,
                w.Track.CornersPerLap,
                w.Track.NumberPitstalls,
                w.Track.PitRoadSpeedLimit,
                w.Track.NightLighting,
                w.WeatherSummaryJson,
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException($"No week {weekNumber} for series {seriesId}.");

        var weather = ScheduleService.MapWeather(week.WeatherSummaryJson);

        // BoP for this week and the previous one (for the week-over-week shift), in one query.
        var bopRows = await db.SeasonCarBops
            .Where(b => b.SeasonId == season.Id
                     && (b.WeekNumber == weekNumber || b.WeekNumber == weekNumber - 1))
            .ToListAsync(ct);

        var currentBop = bopRows.Where(b => b.WeekNumber == weekNumber).ToList();
        var prevBop = bopRows
            .Where(b => b.WeekNumber == weekNumber - 1)
            .ToDictionary(b => b.CarId);

        var carIds = currentBop.Select(b => b.CarId).Distinct().ToList();
        var cars = await db.Cars
            .Where(c => carIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name, c.RainEnabled })
            .ToDictionaryAsync(c => c.Id, ct);

        var fieldRainCapable = cars.Values.Any(c => c.RainEnabled);
        var (riskLevel, riskNote) = StrategyAnalysis.WeatherRisk(
            weather?.PrecipChancePct ?? 0, fieldRainCapable);

        // Personal overlay — only when the caller resolves to a linked iRacing cust_id.
        var custId = userId is null ? null : await member.GetCustIdAsync(userId.Value, ct);
        var personalized = custId is not null and not 0;
        var recsByCar = personalized
            ? (await recommendations.GetRecommendationsAsync(seriesId, weekNumber, custId!.Value, ct: ct))
                .ToDictionary(r => r.CarId)
            : [];

        var carDtos = currentBop
            .Select(b =>
            {
                var prev = prevBop.GetValueOrDefault(b.CarId);
                var (weightDelta, powerDelta, trend) = StrategyAnalysis.BopShift(
                    prev?.WeightPenaltyKg, b.WeightPenaltyKg, prev?.PowerAdjustPct, b.PowerAdjustPct);
                var (fuelCapped, fuelNote) = StrategyAnalysis.FuelNote(b.MaxPctFuelFill);
                var (limitedTires, tireNote) = StrategyAnalysis.TireNote(b.MaxDryTireSets);
                var car = cars.GetValueOrDefault(b.CarId);
                var rec = recsByCar.GetValueOrDefault(b.CarId);

                return new CarStrategyDto(
                    b.CarId,
                    car?.Name ?? $"Car {b.CarId}",
                    b.WeightPenaltyKg,
                    b.PowerAdjustPct,
                    b.MaxPctFuelFill,
                    b.MaxDryTireSets,
                    weightDelta,
                    powerDelta,
                    trend,
                    fuelCapped,
                    fuelNote,
                    limitedTires,
                    tireNote,
                    car?.RainEnabled ?? false,
                    rec?.PercentileRank,
                    rec?.ProjectedLapSeconds,
                    rec?.Rank);
            })
            // Optimal first when personalized; otherwise alphabetical for a stable order.
            .OrderBy(c => c.OptimalRank ?? int.MaxValue)
            .ThenBy(c => c.CarName, StringComparer.Ordinal)
            .ToList();

        return new WeekStrategyDto(
            seriesId,
            seriesName,
            week.WeekNumber,
            week.TrackName,
            week.ConfigName,
            week.LengthMiles,
            week.CornersPerLap,
            week.NumberPitstalls,
            week.PitRoadSpeedLimit,
            week.NightLighting,
            weather,
            new WeatherRiskDto(riskLevel, weather?.PrecipChancePct ?? 0, fieldRainCapable, riskNote),
            personalized,
            carDtos);
    }
}
