using ApexRacers.Data;
using Aydsko.iRacingData;

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
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var iRacingClient = scope.ServiceProvider.GetRequiredService<IDataClient>();

        logger.LogInformation("Ingestion run starting at {Time}", DateTimeOffset.UtcNow);

        var seriesProcessed = 0;
        var lapTimesUpserted = 0;

        // TODO: Step 1 — Fetch active weekly series
        //   Call iRacingClient.GetSeasonsAsync(onlyActive: true, ct) to retrieve the list
        //   of currently active series. Map each result to a Series entity (Id, Name,
        //   CurrentSeason) and upsert into db.Series so the local table stays in sync
        //   with whatever iRacing reports as active this week.

        // TODO: Step 2 — For each series, fetch current week and available cars
        //   For each active Series, call the series schedule endpoint to determine the
        //   current race week: WeekNumber, TrackName, and CarClass. Also retrieve the
        //   list of eligible car IDs for that week. Upsert a Week row (keyed by
        //   SeriesId + SeasonId + WeekNumber) and Car rows. Car.Id is the iRacing-issued
        //   car ID and is treated as a stable external identifier (ValueGeneratedNever).

        // TODO: Step 3 — For each car in the week, fetch lap time results
        //   Call the qualifying / time-trial results endpoint filtered by seasonId,
        //   weekNumber, and carId. Collect the single best (lowest) LapTimeSeconds per
        //   DriverCustomerId — iRacing may return multiple sessions for the same driver.

        // TODO: Step 4 — Upsert LapTimeEntry records
        //   For each (DriverCustomerId, CarId, WeekId) triple, insert a new LapTimeEntry
        //   or update the existing row if the newly fetched lap is faster. Use the composite
        //   indexes on (CarId, WeekId) and (DriverCustomerId, WeekId) for efficient lookups.
        //   Increment lapTimesUpserted for every row written.
        //   Call await db.SaveChangesAsync(ct) in batches to avoid oversized transactions.

        // TODO: Step 5 — Log run summary
        //   After all series are processed, emit a structured log event including
        //   seriesProcessed, lapTimesUpserted, and any per-series error counts so
        //   ingestion runs are observable from logs without querying the database.

        logger.LogInformation(
            "Ingestion run complete at {Time} — series processed: {SeriesProcessed}, lap times upserted: {LapTimesUpserted}",
            DateTimeOffset.UtcNow, seriesProcessed, lapTimesUpserted);
    }
}
