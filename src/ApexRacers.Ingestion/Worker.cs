using System.Text.Json;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using Aydsko.iRacingData;
using Aydsko.iRacingData.Searches;
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

        var seriesProcessed       = 0;
        var subsessionsIndexed    = 0;

        // Steps 2–4 — Process each active season independently so one failure
        // doesn't abort the whole run.
        foreach (var seasonSeries in activeSeries)
        {
            try
            {
                var (series, subsessions) = await ProcessSeasonAsync(
                    db, client, seasonSeries, ct);
                seriesProcessed       += series;
                subsessionsIndexed    += subsessions;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex,
                    "Failed to process season {SeasonId} — skipping",
                    seasonSeries.SeasonId);
            }
        }

        logger.LogInformation(
            "Ingestion run complete at {Time} — series processed: {SeriesProcessed}, subsessions indexed: {SubsessionsIndexed}",
            DateTimeOffset.UtcNow, seriesProcessed, subsessionsIndexed);
    }

    private async Task<(int seriesProcessed, int subsessionsIndexed)> ProcessSeasonAsync(
        AppDbContext db,
        IDataClient client,
        SeasonSeries seasonSeries,
        CancellationToken ct)
    {
        // Series name comes from the first schedule entry; SeriesId is on the root.
        var seriesName = SubsessionIndexer.ResolveSeriesName(
            seasonSeries.Schedules.FirstOrDefault()?.SeriesName, seasonSeries.SeriesId);

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
                Id           = seasonSeries.SeasonId,
                SeriesId     = seasonSeries.SeriesId,
                Year         = seasonSeries.SeasonYear,
                Quarter      = seasonSeries.SeasonQuarter,
                Active       = true,
                LicenseGroup = seasonSeries.LicenseGroup,
                Official     = seasonSeries.Official,
                Drops        = seasonSeries.Drops,
                FixedSetup   = seasonSeries.FixedSetup,
                Multiclass   = seasonSeries.Multiclass,
            });
        }
        else
        {
            season.Active       = true;
            season.LicenseGroup = seasonSeries.LicenseGroup;
            season.Official     = seasonSeries.Official;
            season.Drops        = seasonSeries.Drops;
            season.FixedSetup   = seasonSeries.FixedSetup;
            season.Multiclass   = seasonSeries.Multiclass;
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

        foreach (var item in scheduleResponse.Data.Schedules)
        {
            // Upsert track
            var track = await db.Tracks.FindAsync([item.Track.TrackId], ct);
            if (track is null)
            {
                track = new Track
                {
                    Id         = item.Track.TrackId,
                    Name       = item.Track.TrackName,
                    ConfigName = item.Track.ConfigName ?? "",
                };
                db.Tracks.Add(track);
                await db.SaveChangesAsync(ct);
            }
            else
            {
                track.Name       = item.Track.TrackName;
                track.ConfigName = item.Track.ConfigName ?? "";
            }

            var week = await db.Weeks
                .FirstOrDefaultAsync(
                    w => w.SeasonId == seasonSeries.SeasonId && w.WeekNumber == item.RaceWeekNum, ct);

            if (week is null)
            {
                week = new Week
                {
                    SeasonId   = seasonSeries.SeasonId,
                    WeekNumber = item.RaceWeekNum,
                    TrackId    = item.Track.TrackId,
                    StartDate  = item.StartDate,
                };
                db.Weeks.Add(week);
                await db.SaveChangesAsync(ct); // flush to get DB-generated Week.Id
            }
            else
            {
                week.TrackId   = item.Track.TrackId;
                week.StartDate = item.StartDate;
            }

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

        // ── Step 4: Index new race subsessions ────────────────────────────────────
        var indexed = await IndexNewSubsessionsAsync(db, client, seasonSeries, ct);

        // Upsert SeasonCarClass entries after indexing so new CarClass rows created
        // during subsession indexing are available for the FK guard.
        foreach (var carClassId in seasonSeries.CarClassIds)
        {
            if (await db.SeasonCarClasses.FindAsync([seasonSeries.SeasonId, carClassId], ct) is null
                && await db.CarClasses.FindAsync([carClassId], ct) is not null)
            {
                db.SeasonCarClasses.Add(new SeasonCarClass
                {
                    SeasonId   = seasonSeries.SeasonId,
                    CarClassId = carClassId,
                });
            }
        }

        await db.SaveChangesAsync(ct);

        return (1, indexed);
    }

    private async Task<int> IndexNewSubsessionsAsync(
        AppDbContext db, IDataClient client, SeasonSeries seasonSeries, CancellationToken ct)
    {
        // Narrow the search to sessions starting after the last indexed one (minus a
        // 1-hour buffer for concurrent splits). Null on first run → full season fetch.
        var lastIndexedStart = await db.Subsessions
            .Where(s => s.SeasonId == seasonSeries.SeasonId)
            .MaxAsync(s => (DateTimeOffset?)s.StartTime, ct);

        DateTime? searchRangeBegin = SubsessionIndexer.ComputeSearchRangeBegin(lastIndexedStart);

        var searchResponse = await client.SearchOfficialResultsAsync(new OfficialSearchParameters
        {
            SeriesId        = seasonSeries.SeriesId,
            SeasonYear      = seasonSeries.SeasonYear,
            SeasonQuarter   = seasonSeries.SeasonQuarter,
            EventTypes      = new[] { 5 },  // Race only
            OfficialOnly    = true,
            StartRangeBegin = searchRangeBegin,
        }, ct);

        if (searchResponse?.Data.Items is not { Length: > 0 }) return 0;

        var candidateIds = searchResponse.Data.Items
            .Select(i => i.SubsessionId)
            .ToHashSet();

        var existingIds = await db.Subsessions
            .Where(s => candidateIds.Contains(s.Id))
            .Select(s => s.Id)
            .ToHashSetAsync(ct);

        var newIds = SubsessionIndexer.ComputeNewSubsessionIds(candidateIds, existingIds);

        int stored = 0;
        foreach (var subsessionId in newIds)
        {
            try
            {
                var resultResponse = await client.GetSubSessionResultAsync(subsessionId, includeLicenses: false, ct);
                if (resultResponse?.Data is null) continue;

                var data = resultResponse.Data;

                // Find race simsession
                var raceSession = data.SessionResults
                    .FirstOrDefault(s => s.SimSessionType == 6);
                if (raceSession is null) continue;

                // Resolve WeekId from DB (RaceWeekIndex is 0-based week number)
                var weekId = await db.Weeks
                    .Where(w => w.SeasonId == data.SeasonId && w.WeekNumber == data.RaceWeekIndex)
                    .Select(w => (Guid?)w.Id)
                    .FirstOrDefaultAsync(ct);

                if (weekId is null)
                {
                    logger.LogWarning(
                        "Week not found for season {SeasonId} week {WeekIndex} — skipping subsession {SubsessionId}",
                        data.SeasonId, data.RaceWeekIndex, subsessionId);
                    continue;
                }

                // Upsert track if unknown
                if (await db.Tracks.FindAsync([data.Track.TrackId], ct) is null)
                {
                    db.Tracks.Add(new Track
                    {
                        Id         = data.Track.TrackId,
                        Name       = data.Track.TrackName,
                        ConfigName = data.Track.ConfigName ?? "",
                    });
                    await db.SaveChangesAsync(ct);
                }

                // Determine split number from session_splits order
                var splitNum = SubsessionIndexer.ResolveSplitNumber(
                    data.SessionSplits?.Select(s => s.SubSessionId).ToList(), subsessionId);

                var subsession = new Subsession
                {
                    Id                     = subsessionId,
                    SeasonId               = data.SeasonId,
                    WeekNumber             = data.RaceWeekIndex,
                    WeekId                 = weekId,
                    TrackId                = data.Track.TrackId,
                    OfficialSession        = data.OfficialSession,
                    EventStrengthOfField   = data.EventStrengthOfField,
                    StartTime              = data.StartTime,
                    EndTime                = data.EndTime,   // DateTimeOffset, not nullable in SubSessionResult
                    SplitNum               = splitNum,
                    NumCautions            = data.NumberOfCautions,
                    NumCautionLaps         = data.NumberOfCautionLaps,
                    NumLeadChanges         = data.NumberOfLeadChanges,
                    CornersPerLap          = data.CornersPerLap,
                    EventAverageLapSeconds = SubsessionIndexer.EventLapSecondsOrSentinel(data.EventAverageLap),
                    EventBestLapSeconds    = SubsessionIndexer.EventLapSecondsOrSentinel(data.EventBestLapTime),
                    EventLapsComplete      = data.EventLapsComplete,
                    WeatherJson            = data.Weather is null ? null : JsonSerializer.Serialize(data.Weather),
                    TrackStateJson         = data.TrackState is null ? null : JsonSerializer.Serialize(data.TrackState),
                };
                db.Subsessions.Add(subsession);
                await db.SaveChangesAsync(ct);

                foreach (var r in raceSession.Results)
                {
                    // Skip AI drivers and team events (null CustomerId)
                    if (SubsessionIndexer.ShouldSkipResult(r.AI, r.CustomerId)) continue;

                    var bestLapSecs = SubsessionIndexer.LapSecondsOrSentinel(r.BestLapTime);
                    var avgLapSecs  = SubsessionIndexer.LapSecondsOrSentinel(r.AverageLap);

                    // Upsert car
                    if (await db.Cars.FindAsync([r.CarId], ct) is null)
                    {
                        db.Cars.Add(new Car
                        {
                            Id              = r.CarId,
                            Name            = r.CarName,
                            NameAbbreviated = r.CarName,
                        });
                    }

                    // Upsert car class
                    if (await db.CarClasses.FindAsync([r.CarClassId], ct) is null)
                    {
                        db.CarClasses.Add(new CarClass
                        {
                            Id            = r.CarClassId,
                            Name          = r.CarClassName,
                            ShortName     = r.CarClassShortName,
                            RelativeSpeed = 0,
                        });
                    }

                    db.SubsessionResults.Add(new SubsessionResult
                    {
                        SubsessionId              = subsessionId,
                        CustId                    = (long)r.CustomerId!.Value,
                        CarId                     = r.CarId,
                        CarClassId                = r.CarClassId,
                        FinishPosition            = r.FinishPosition,
                        FinishPositionInClass     = r.FinishPositionInClass,
                        StartingPosition          = r.StartingPosition,
                        StartingPositionInClass   = r.StartingPositionInClass ?? 0,
                        Incidents                 = r.Incidents,
                        BestLapSeconds            = bestLapSecs,
                        AverageLapSeconds         = avgLapSecs,
                        LapsComplete              = r.LapsComplete,
                        LapsLead                  = r.LapsLead,
                        ChampPoints               = r.ChampPoints,
                        AggregateChampPoints      = r.AggregateChampionshipPoints,
                        NewIRating                = r.NewIRating,
                        OldIRating                = r.OldIRating,
                        NewCpi                    = (double)r.NewCornersPerIncident,
                        OldCpi                    = (double)r.OldCornersPerIncident,
                        ReasonOut                 = r.ReasonOut,
                        ReasonOutId               = r.ReasonOutId,
                        Division                  = r.Division,
                        DropRace                  = r.DropRace,
                        Interval                  = r.ClassInterval?.TotalSeconds ?? -1,
                        DisplayName               = r.DisplayName,
                        QualLapSeconds            = SubsessionIndexer.LapSecondsOrSentinel(r.QualifyingLapTime),
                        NewSubLevel               = r.NewSubLevel,
                        OldSubLevel               = r.OldSubLevel,
                        NewTtRating               = r.NewTimeTrialRating,
                        OldTtRating               = r.OldTimeTrialRating,
                    });
                }

                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
                stored++;

                logger.LogDebug(
                    "Indexed subsession {SubsessionId} (season {SeasonId} week {WeekIndex})",
                    subsessionId, data.SeasonId, data.RaceWeekIndex);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex,
                    "Failed to index subsession {SubsessionId} — skipping", subsessionId);
            }
        }

        return stored;
    }
}
