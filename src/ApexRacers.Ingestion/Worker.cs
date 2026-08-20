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

        // Step 1b — Refresh the full car/track catalog (specs + assets) for the catalog explorer.
        try
        {
            var (cars, tracks) = await RefreshCatalogAsync(db, client, ct);
            logger.LogDebug("Catalog refreshed: {Cars} cars, {Tracks} tracks", cars, tracks);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Catalog refresh failed — skipping");
        }

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

    // Upsert the full car & track catalog (+ asset image bits) so the catalog explorer reads it
    // from the DB. Pure mapping lives in CatalogIngest (tested); this is just fetch + upsert.
    private static async Task<(int cars, int tracks)> RefreshCatalogAsync(
        AppDbContext db, IDataClient client, CancellationToken ct)
    {
        var cars = (await client.GetCarsAsync(ct)).Data;
        var carAssets = (await client.GetCarAssetDetailsAsync(ct)).Data;
        foreach (var info in cars)
        {
            var car = await db.Cars.FindAsync([info.CarId], ct);
            if (car is null)
            {
                car = new Car { Id = info.CarId, Name = info.CarName, NameAbbreviated = info.CarNameAbbreviated };
                db.Cars.Add(car);
            }
            carAssets.TryGetValue(info.CarId.ToString(), out var asset);
            CatalogIngest.PopulateCar(car, info, asset);
        }

        var tracks = (await client.GetTracksAsync(ct)).Data;
        var trackAssets = (await client.GetTrackAssetsAsync(ct)).Data;
        foreach (var info in tracks)
        {
            var track = await db.Tracks.FindAsync([info.TrackId], ct);
            if (track is null)
            {
                track = new Track { Id = info.TrackId, Name = info.TrackName };
                db.Tracks.Add(track);
            }
            trackAssets.TryGetValue(info.TrackId.ToString(), out var asset);
            CatalogIngest.PopulateTrack(track, info, asset);
        }

        await db.SaveChangesAsync(ct);
        return (cars.Length, tracks.Length);
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

        var seasonIngest = new SeasonIngest(db);
        await seasonIngest.UpsertHeaderAsync(seasonSeries, seriesName, ct);

        // ── Step 3: Fetch full schedule → upsert Weeks, Cars, SeasonCars ─────────
        // GetSeasonScheduleAsync gives us SeasonScheduleItem[] with per-week car lists.
        var scheduleResponse = await client.GetSeasonScheduleAsync(seasonSeries.SeasonId, ct);
        if (scheduleResponse?.Data?.Schedules is not { Length: > 0 })
        {
            logger.LogDebug("Season {SeasonId} returned no schedule items", seasonSeries.SeasonId);
            return (0, 0);
        }

        await seasonIngest.UpsertScheduleAsync(
            seasonSeries.SeasonId, scheduleResponse.Data.Schedules, ct);

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

                // Rank this subsession among its Race Session's splits by SOF, not by array order.
                var splitPosition = SubsessionIndexer.ResolveSplitPosition(
                    data.SessionSplits?
                        .Select(s => new SubsessionIndexer.SplitEntry(s.SubSessionId, s.EventStrengthOfField))
                        .ToList(),
                    subsessionId);

                if (splitPosition is null && data.SessionSplits is { Length: > 0 } splits)
                {
                    logger.LogWarning(
                        "Subsession {SubsessionId} is absent from its own session_splits ({SplitCount} entries) — storing an unknown Split Index",
                        subsessionId, splits.Length);
                }

                var subsession = SubsessionMapper.ToEntity(
                    subsessionId, data, weekId, splitPosition);
                db.Subsessions.Add(subsession);
                await db.SaveChangesAsync(ct);

                foreach (var r in raceSession.Results)
                {
                    // Skip AI drivers and team events (null CustomerId)
                    if (SubsessionIndexer.ShouldSkipResult(r.AI, r.CustomerId)) continue;

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

                    db.SubsessionResults.Add(SubsessionMapper.ToResult(subsessionId, r));
                }

                await db.SaveChangesAsync(ct);

                // A season can contain thousands of result rows. Detach the stored subsession graph
                // after each successful write so EF does not retain every prior race and driver in
                // the change tracker for the rest of this long-lived ingestion run.
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
