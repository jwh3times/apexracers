using ApexRacers.Core.Models;
using ApexRacers.Data;
using Aydsko.iRacingData;
using Aydsko.iRacingData.Series;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Ingestion;

public sealed class Worker(
    ILogger<Worker> logger,
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration) : BackgroundService
{
    private readonly TimeSpan _interval =
        TimeSpan.FromMinutes(configuration.GetValue("INGESTION_INTERVAL_MINUTES", 60));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Ingestion worker started. Run interval: {IntervalMinutes} minutes",
            _interval.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Ingestion run failed — will retry after interval");
            }

            await Task.Delay(_interval, stoppingToken);
        }

        logger.LogInformation("Ingestion worker stopped");
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db     = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var client = scope.ServiceProvider.GetRequiredService<IDataClient>();

        logger.LogInformation("Ingestion run starting at {Time}", DateTimeOffset.UtcNow);

        // Step 1 — Fetch all active seasons (includes per-week schedule/track info).
        var seasonsResponse = await client.GetSeasonsAsync(includeSeries: true, ct);
        var activeSeries = seasonsResponse.Data.Where(s => s.Active).ToList();
        logger.LogDebug("Found {Count} active series", activeSeries.Count);

        // Fetch car-class → car-IDs lookup once for the whole run.
        var carClassesResponse = await client.GetCarClassesAsync(ct);
        var carClassToCarIds = carClassesResponse.Data.ToDictionary(
            cc => cc.CarClassId,
            cc => cc.CarsInClass.Where(c => !c.Retired).Select(c => c.CarId).ToList());

        var seriesProcessed  = 0;
        var lapTimesUpserted = 0;

        // Steps 2–5 — Process each active season independently so one failure
        // doesn't abort the whole run.
        foreach (var seasonSeries in activeSeries)
        {
            try
            {
                var (series, laps) = await ProcessSeasonAsync(
                    db, client, seasonSeries, carClassToCarIds, ct);
                seriesProcessed  += series;
                lapTimesUpserted += laps;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex,
                    "Failed to process season {SeasonId} — skipping",
                    seasonSeries.SeasonId);
            }
        }

        logger.LogInformation(
            "Ingestion run complete at {Time} — series processed: {SeriesProcessed}, lap times upserted: {LapTimesUpserted}",
            DateTimeOffset.UtcNow, seriesProcessed, lapTimesUpserted);
    }

    private async Task<(int seriesProcessed, int lapTimesUpserted)> ProcessSeasonAsync(
        AppDbContext db,
        IDataClient client,
        SeasonSeries seasonSeries,
        Dictionary<int, List<int>> carClassToCarIds,
        CancellationToken ct)
    {
        // Series name comes from the first schedule entry; SeriesId is on the root.
        var seriesName = seasonSeries.Schedules.FirstOrDefault()?.SeriesName
                         ?? $"Series {seasonSeries.SeriesId}";

        // ── Step 2: Upsert Series + Season ────────────────────────────────────────

        var series = await db.Series.FindAsync([seasonSeries.SeriesId], ct);
        if (series is null)
            db.Series.Add(new Series { Id = seasonSeries.SeriesId, Name = seriesName });
        else
            series.Name = seriesName;

        var season = await db.Seasons.FindAsync([seasonSeries.SeasonId], ct);
        if (season is null)
        {
            db.Seasons.Add(new Season
            {
                Id        = seasonSeries.SeasonId,
                SeriesId  = seasonSeries.SeriesId,
                Year      = seasonSeries.SeasonYear,
                Quarter   = seasonSeries.SeasonQuarter,
                Active    = true,
            });
        }
        else
        {
            season.Active = true;
        }

        await db.SaveChangesAsync(ct);

        // ── Step 3: Fetch full schedule → upsert Weeks, Cars, SeasonCars ─────────
        // GetSeasonScheduleAsync gives us SeasonScheduleItem[] with per-week car lists.
        var scheduleResponse = await client.GetSeasonScheduleAsync(seasonSeries.SeasonId, ct);
        if (scheduleResponse?.Data?.Schedules is not { Length: > 0 })
        {
            logger.LogDebug("Season {SeasonId} returned no schedule items", seasonSeries.SeasonId);
            return (0, 0);
        }

        var weekByIndex = new Dictionary<int, Week>(); // raceWeekNum → Week entity

        foreach (var item in scheduleResponse.Data.Schedules)
        {
            var week = await db.Weeks
                .FirstOrDefaultAsync(
                    w => w.SeasonId == seasonSeries.SeasonId && w.WeekNumber == item.RaceWeekNum, ct);

            if (week is null)
            {
                week = new Week
                {
                    SeasonId       = seasonSeries.SeasonId,
                    WeekNumber     = item.RaceWeekNum,
                    IracingTrackId = item.Track.TrackId,
                    TrackName      = item.Track.TrackName,
                    ConfigName     = item.Track.ConfigName ?? "",
                    StartDate      = item.StartDate,
                };
                db.Weeks.Add(week);
                await db.SaveChangesAsync(ct); // flush to get DB-generated Week.Id
            }
            else
            {
                week.IracingTrackId = item.Track.TrackId;
                week.TrackName      = item.Track.TrackName;
                week.ConfigName     = item.Track.ConfigName ?? "";
                week.StartDate      = item.StartDate;
            }

            weekByIndex[item.RaceWeekNum] = week;

            foreach (var car in item.RaceWeekCars)
            {
                if (await db.Cars.FindAsync([car.CarId], ct) is null)
                {
                    db.Cars.Add(new Car
                    {
                        Id              = car.CarId,
                        Name            = car.CarName,
                        NameAbbreviated = car.CarNameAbbreviated,
                    });
                }

                if (await db.SeasonCars.FindAsync([seasonSeries.SeasonId, car.CarId], ct) is null)
                {
                    db.SeasonCars.Add(new SeasonCar
                    {
                        SeasonId = seasonSeries.SeasonId,
                        CarId    = car.CarId,
                    });
                }
            }
        }

        await db.SaveChangesAsync(ct);

        // ── Step 4: Fetch TT results for the current race week ────────────────────

        var currentWeekIndex = seasonSeries.RaceWeek; // 0-based
        if (!weekByIndex.TryGetValue(currentWeekIndex, out var currentWeek))
        {
            logger.LogDebug(
                "Season {SeasonId}: current week index {Week} not in schedule",
                seasonSeries.SeasonId, currentWeekIndex);
            return (1, 0);
        }

        var currentItem = scheduleResponse.Data.Schedules
            .FirstOrDefault(s => s.RaceWeekNum == currentWeekIndex);
        if (currentItem is null) return (1, 0);

        var weekCarIds = currentItem.RaceWeekCars.Select(c => c.CarId).ToHashSet();

        var lapTimesUpserted = 0;

        foreach (var carClassId in seasonSeries.CarClassIds)
        {
            // Resolve the single car for this class that ran this week.
            // Multi-car classes in TT are rare; skip them for now since the API
            // result doesn't tell us which car each driver used.
            var carsThisWeek = carClassToCarIds.GetValueOrDefault(carClassId, [])
                .Where(id => weekCarIds.Contains(id))
                .ToList();

            if (carsThisWeek.Count != 1)
            {
                logger.LogDebug(
                    "Season {SeasonId} class {ClassId} has {Count} cars in week {Week} — skipping",
                    seasonSeries.SeasonId, carClassId, carsThisWeek.Count, currentWeekIndex);
                continue;
            }

            var carId = carsThisWeek[0];

            var ttResponse = await client.GetSeasonTimeTrialResultsAsync(
                seasonSeries.SeasonId, carClassId, currentWeekIndex, division: null, ct);

            if (ttResponse?.Data.Results is not { Length: > 0 })
            {
                logger.LogDebug(
                    "No TT results for season {SeasonId} class {ClassId} week {Week}",
                    seasonSeries.SeasonId, carClassId, currentWeekIndex);
                continue;
            }

            // ── Step 5: Upsert LapTimeEntry records ───────────────────────────────
            // Batch-load all existing entries for this (week, car) to avoid one
            // DB round-trip per driver.
            var existing = await db.LapTimeEntries
                .Where(l => l.WeekId == currentWeek.Id && l.CarId == carId)
                .ToDictionaryAsync(l => l.DriverCustomerId, ct);

            foreach (var result in ttResponse.Data.Results)
            {
                if (result.BestNlapsTime is null) continue;

                var lapSecs = result.BestNlapsTime.Value.TotalSeconds;

                if (existing.TryGetValue(result.CustomerId, out var entry))
                {
                    if (lapSecs < entry.LapTimeSeconds)
                    {
                        entry.LapTimeSeconds = lapSecs;
                        entry.RecordedAt     = DateTimeOffset.UtcNow;
                        lapTimesUpserted++;
                    }
                }
                else
                {
                    var newEntry = new LapTimeEntry
                    {
                        DriverCustomerId = result.CustomerId,
                        CarId            = carId,
                        WeekId           = currentWeek.Id,
                        LapTimeSeconds   = lapSecs,
                        RecordedAt       = DateTimeOffset.UtcNow,
                    };
                    db.LapTimeEntries.Add(newEntry);
                    existing[result.CustomerId] = newEntry; // prevent duplicate add within batch
                    lapTimesUpserted++;
                }
            }

            await db.SaveChangesAsync(ct);

            logger.LogDebug(
                "Season {SeasonId} class {ClassId} week {Week}: processed {Count} TT results",
                seasonSeries.SeasonId, carClassId, currentWeekIndex,
                ttResponse.Data.Results.Length);
        }

        return (1, lapTimesUpserted);
    }
}
