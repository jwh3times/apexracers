using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Seeder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

// ── Configuration ─────────────────────────────────────────────────────────────
var config = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .Build();

var connectionString =
    config["DATABASE_CONNECTION_STRING"]
    ?? "Host=localhost;Port=5432;Database=apexracers;Username=apexracers;Password=devpassword";

var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseNpgsql(connectionString)
    .Options;

await using var db = new AppDbContext(options);

Console.WriteLine("ApexRacers Seeder — connecting to database…");

// ── Driver pool ───────────────────────────────────────────────────────────────
// 200 fake driver customer IDs. Each driver gets a deterministic skill factor
// so their lap times are consistent across all series.
const int DriverStart = 100_001;
const int DriverCount = 200;

var driverSkillFactors = new Dictionary<long, double>();
for (int i = 0; i < DriverCount; i++)
{
    long id = DriverStart + i;
    driverSkillFactors[id] = ComputeSkillFactor(id);
}

// ── Main seeding loop ─────────────────────────────────────────────────────────
int totalLapTimes = 0;

foreach (var series in SeedData.AllSeries)
{
    Console.WriteLine($"  Seeding: {series.Name}");

    await UpsertMetadataAsync(db, series);

    // Resolve the Week DB-generated GUIDs after metadata is saved
    var weekIds = await db.Weeks
        .Where(w => w.SeasonId == series.SeasonId)
        .ToDictionaryAsync(w => w.WeekNumber, w => w.Id);

    // Collect all cars for this series (main class + faster class if any)
    var allCars = series.FasterClassCars is null
        ? series.Cars
        : [.. series.Cars, .. series.FasterClassCars];

    foreach (var week in series.Weeks)
    {
        if (!weekIds.TryGetValue(week.WeekNumber, out var weekId))
            continue;

        foreach (var car in allCars)
        {
            // Skip if already seeded for this (week, car) combination
            bool alreadySeeded = await db.LapTimeEntries
                .AnyAsync(l => l.WeekId == weekId && l.CarId == car.CarId);
            if (alreadySeeded)
                continue;

            // Determine if this car is in the faster class
            bool isFasterClass = series.FasterClassCars?.Any(c => c.CarId == car.CarId) ?? false;
            double baseLap = week.BaseLapSeconds
                + car.OffsetSeconds
                + (isFasterClass ? week.FasterClassOffset : 0.0);

            var entries = new List<LapTimeEntry>(DriverCount);
            foreach (var (driverId, skillFactor) in driverSkillFactors)
            {
                double lapTime = GenerateLapTime(driverId, car.CarId, week.WeekNumber, baseLap, skillFactor, week.StdDevSeconds);
                entries.Add(new LapTimeEntry
                {
                    DriverCustomerId = driverId,
                    CarId            = car.CarId,
                    WeekId           = weekId,
                    LapTimeSeconds   = lapTime,
                    RecordedAt       = week.StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                });
            }

            db.LapTimeEntries.AddRange(entries);
            await db.SaveChangesAsync();
            totalLapTimes += entries.Count;
        }
    }

    Console.WriteLine($"    Done");
}

Console.WriteLine($"\nSeeding complete — {totalLapTimes:N0} lap time entries written.");

// ── Helpers ───────────────────────────────────────────────────────────────────

static async Task UpsertMetadataAsync(AppDbContext db, SeriesDef series)
{
    // Series
    var seriesEntity = await db.Series.FindAsync(series.SeriesId);
    if (seriesEntity is null)
        db.Series.Add(new Series { Id = series.SeriesId, Name = series.Name });
    else
        seriesEntity.Name = series.Name;

    // Season
    var season = await db.Seasons.FindAsync(series.SeasonId);
    if (season is null)
    {
        db.Seasons.Add(new Season
        {
            Id       = series.SeasonId,
            SeriesId = series.SeriesId,
            Year     = series.Year,
            Quarter  = series.Quarter,
            Active   = true,
        });
    }
    else
    {
        season.Active = true;
    }

    await db.SaveChangesAsync();

    // Cars
    var allCars = series.FasterClassCars is null
        ? series.Cars
        : [.. series.Cars, .. series.FasterClassCars];

    foreach (var car in allCars)
    {
        if (await db.Cars.FindAsync(car.CarId) is null)
        {
            db.Cars.Add(new Car
            {
                Id              = car.CarId,
                Name            = car.Name,
                NameAbbreviated = car.Abbr,
            });
        }

        if (await db.SeasonCars.FindAsync(series.SeasonId, car.CarId) is null)
        {
            db.SeasonCars.Add(new SeasonCar
            {
                SeasonId = series.SeasonId,
                CarId    = car.CarId,
            });
        }
    }

    // Weeks
    foreach (var week in series.Weeks)
    {
        var existing = await db.Weeks.FirstOrDefaultAsync(
            w => w.SeasonId == series.SeasonId && w.WeekNumber == week.WeekNumber);

        if (existing is null)
        {
            db.Weeks.Add(new Week
            {
                SeasonId       = series.SeasonId,
                WeekNumber     = week.WeekNumber,
                IracingTrackId = week.TrackId,
                TrackName      = week.TrackName,
                ConfigName     = week.ConfigName,
                StartDate      = week.StartDate,
            });
        }
        else
        {
            existing.IracingTrackId = week.TrackId;
            existing.TrackName      = week.TrackName;
            existing.ConfigName     = week.ConfigName;
            existing.StartDate      = week.StartDate;
        }
    }

    await db.SaveChangesAsync();
}

// Deterministic skill factor for a driver: 0 = fastest, 1 = slowest.
// Distribution is approximately normal (mean 0.55, stdDev 0.2), clipped to [0,1].
static double ComputeSkillFactor(long driverId)
{
    var rng = new Random((int)(driverId ^ (driverId >> 16)));
    return Math.Clamp(NextGaussian(rng, 0.55, 0.20), 0.0, 1.0);
}

// Generates a lap time.
// The driver's skill factor linearly maps to +/- 2.5s from the base time.
// A per-lap gaussian noise adds ±(stdDev * 0.3) for natural variation.
static double GenerateLapTime(
    long driverId, int carId, int weekNumber,
    double baseLapSeconds, double skillFactor, double stdDev)
{
    int seed = HashCode.Combine((int)(driverId & 0x7FFFFFFF), carId, weekNumber);
    var rng = new Random(seed);
    double lapTime = baseLapSeconds
        + ((skillFactor - 0.5) * 5.0)
        + NextGaussian(rng, 0.0, stdDev * 0.3);

    // Floor at base - 3s so even the fastest seeded drivers stay realistic
    return Math.Max(lapTime, baseLapSeconds - 3.0);
}

// Box-Muller transform for a normally distributed random double.
static double NextGaussian(Random rng, double mean, double stdDev)
{
    double u1 = 1.0 - rng.NextDouble();
    double u2 = 1.0 - rng.NextDouble();
    double z  = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    return mean + stdDev * z;
}
