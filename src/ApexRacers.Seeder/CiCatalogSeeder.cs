using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Seeder;

/// <summary>
/// Ids seeded by CiCatalogSeeder. web/e2e/a11y-gated.spec.ts mirrors these constants —
/// keep them in sync if anything changes.
/// </summary>
public static class CiCatalog
{
    public const int SeriesId   = 9900;
    public const int SeasonId   = 99001;
    public const int CarClassId = 9901;
    public const int WeekCount  = 8;
    public const int CurrentRaceWeekIndex = 4; // Race Week whose date window contains "today"
    public const long DriverStart = 100_001;   // includes DemoData.DriverCustId + RivalCustId
    public const int DriverCount  = 60;

    public static readonly int[] CarIds   = [9911, 9912, 9913, 9914, 9915, 9916];
    public static readonly int[] TrackIds = [9951, 9952, 9953, 9954, 9955, 9956, 9957, 9958];
}

/// <summary>
/// Seeds a fully synthetic catalog (series/season/weeks/tracks/cars/classes) plus
/// per-car-per-week subsessions and results, with NO dependency on the gitignored
/// private/iracing-api-response-objects/. Built for CI (E2E gated-page audits): after
/// this, DemoCacheSeeder (--demo) can fill the demo cache exactly as it does locally.
/// Idempotent — every step checks before inserting.
/// </summary>
public sealed class CiCatalogSeeder(AppDbContext db)
{
    private static readonly string[] CarNames =
        ["Apex GT3 Falcon", "Apex GT3 Kestrel", "Apex GT3 Harrier",
         "Apex GT3 Osprey", "Apex GT3 Merlin", "Apex GT3 Condor"];

    private static readonly (string Name, string Config, double Length, int Corners)[] TrackSpecs =
        [("Cypress International Circuit", "Grand Prix", 3.6, 17),
         ("Granite Peak Raceway",          "Full Course", 2.8, 14),
         ("Harbor City Street Circuit",    "Street", 2.2, 12),
         ("Sierra Grande GP",              "Grand Prix", 3.9, 20),
         ("Lakeshore Motorsports Park",    "Full Course", 2.5, 13),
         ("Redrock Speedway Road Course",  "Road Course", 3.1, 15),
         ("Northgate Circuit",             "Grand Prix", 2.9, 16),
         ("Willow Basin Raceway",          "Full Course", 3.4, 18)];

    private const double AvgSpeedMph = 90.0; // GT3-ish baseline

    public async Task SeedAsync()
    {
        await SeedCatalogAsync();
        await SeedRacesAsync();
    }

    private async Task SeedCatalogAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        if (await db.Series.FindAsync(CiCatalog.SeriesId) is null)
            db.Series.Add(new Series
            {
                Id = CiCatalog.SeriesId,
                Name = "Apex Demo GT Championship",
                CategoryId = 2,
                Category = "road",
                LicenseGroup = 3,
                Official = true,
            });

        if (await db.Seasons.FindAsync(CiCatalog.SeasonId) is null)
            db.Seasons.Add(new Season
            {
                Id = CiCatalog.SeasonId,
                SeriesId = CiCatalog.SeriesId,
                Year = today.Year,
                Quarter = ((today.Month - 1) / 3) + 1,
                Active = true,
                LicenseGroup = 3,
                Official = true,
                Drops = 2,
                FixedSetup = true,
                Multiclass = false,
            });

        if (await db.CarClasses.FindAsync(CiCatalog.CarClassId) is null)
            db.CarClasses.Add(new CarClass
            {
                Id = CiCatalog.CarClassId,
                Name = "Apex Demo GT3",
                ShortName = "Demo GT3",
                RelativeSpeed = 52,
            });

        for (var i = 0; i < CiCatalog.CarIds.Length; i++)
        {
            var carId = CiCatalog.CarIds[i];
            if (await db.Cars.FindAsync(carId) is not null) continue;
            db.Cars.Add(new Car
            {
                Id = carId,
                Name = CarNames[i],
                NameAbbreviated = $"GT3-{i + 1}",
                Retired = false,
                FreeWithSubscription = i == 0,
                Hp = 520 + (i * 10),
                CarWeight = 2800 + (i * 25),
                CarMake = "Apex",
                CarModel = CarNames[i].Replace("Apex GT3 ", ""),
                RainEnabled = true,
                CategoriesJson = "[\"road\"]",
            });
        }

        for (var i = 0; i < CiCatalog.TrackIds.Length; i++)
        {
            var trackId = CiCatalog.TrackIds[i];
            if (await db.Tracks.FindAsync(trackId) is not null) continue;
            var spec = TrackSpecs[i];
            db.Tracks.Add(new Track
            {
                Id = trackId,
                Name = spec.Name,
                ConfigName = ConfigurationName.Normalize(spec.Config),
                CategoryId = 2,
                Category = "road",
                TrackConfigLength = spec.Length,
                IsDirt = false,
                IsOval = false,
                Location = "Demo Valley, USA",
                TimeZone = "America/New_York",
                Retired = false,
                CornersPerLap = spec.Corners,
                PitRoadSpeedLimit = 45,
                NumberPitstalls = 30,
                NightLighting = i % 2 == 0,
                HasSvgMap = false,
            });
        }

        await db.SaveChangesAsync();

        foreach (var carId in CiCatalog.CarIds)
            if (await db.CarClassCars.FindAsync(CiCatalog.CarClassId, carId) is null)
                db.CarClassCars.Add(new CarClassCar { CarClassId = CiCatalog.CarClassId, CarId = carId });

        foreach (var carId in CiCatalog.CarIds)
            if (await db.SeasonCars.FindAsync(CiCatalog.SeasonId, carId) is null)
                db.SeasonCars.Add(new SeasonCar { SeasonId = CiCatalog.SeasonId, CarId = carId });

        if (await db.SeasonCarClasses.FindAsync(CiCatalog.SeasonId, CiCatalog.CarClassId) is null)
            db.SeasonCarClasses.Add(new SeasonCarClass
            {
                SeasonId = CiCatalog.SeasonId,
                CarClassId = CiCatalog.CarClassId,
            });

        // Each Race Week Index starts relative to CurrentRaceWeekIndex, anchored 3 days back so
        // the current Race Week window always contains "today" for current-week UI logic.
        for (var raceWeekIndex = 1; raceWeekIndex <= CiCatalog.WeekCount; raceWeekIndex++)
        {
            var exists = await db.Weeks.AnyAsync(
                w => w.SeasonId == CiCatalog.SeasonId && w.RaceWeekIndex == raceWeekIndex);
            if (exists) continue;
            db.Weeks.Add(new Week
            {
                SeasonId = CiCatalog.SeasonId,
                RaceWeekIndex = raceWeekIndex,
                TrackId = CiCatalog.TrackIds[raceWeekIndex - 1],
                StartDate = today.AddDays(((raceWeekIndex - CiCatalog.CurrentRaceWeekIndex) * 7) - 3),
            });
        }

        await db.SaveChangesAsync();
        Console.WriteLine($"  Catalog: 1 series, 1 season, {CiCatalog.WeekCount} weeks, "
            + $"{CiCatalog.CarIds.Length} cars, {CiCatalog.TrackIds.Length} tracks.");
    }

    private async Task SeedRacesAsync()
    {
        var driverSkill = Enumerable.Range(0, CiCatalog.DriverCount)
            .ToDictionary(
                i => CiCatalog.DriverStart + i,
                i => SyntheticLaps.ComputeSkillFactor(CiCatalog.DriverStart + i));

        var weeks = await db.Weeks
            .Where(w => w.SeasonId == CiCatalog.SeasonId)
            .ToListAsync();

        int subsessions = 0, results = 0;

        foreach (var week in weeks.OrderBy(w => w.RaceWeekIndex))
        {
            var trackLength = TrackSpecs[week.RaceWeekIndex - 1].Length;
            var baseLap = trackLength / AvgSpeedMph * 3600.0;
            var stdDev = Math.Max(1.0, baseLap * 0.02);

            for (var carIndex = 0; carIndex < CiCatalog.CarIds.Length; carIndex++)
            {
                var carId = CiCatalog.CarIds[carIndex];
                // Same id formula as the main seeder: unique per (season, week, car), negative.
                var subsessionId = -((CiCatalog.SeasonId * 10000) + (week.RaceWeekIndex * 100) + carIndex);

                if (await db.Subsessions.AnyAsync(s => s.Id == subsessionId)) continue;

                db.Subsessions.Add(new Subsession
                {
                    Id = subsessionId,
                    SeasonId = CiCatalog.SeasonId,
                    RaceWeekIndex = week.RaceWeekIndex,
                    WeekId = week.Id,
                    TrackId = week.TrackId,
                    OfficialSession = true,
                    EventStrengthOfField = 1500,
                    // The sole Split of its Race Session, so index 0 of a count of 1.
                    SplitIndex = 0,
                    SplitCount = 1,
                    // Every synthetic entry names one Driver, so the field is complete.
                    TeamEntryCount = 0,
                    AiEntryCount = 0,
                    StartTime = new DateTimeOffset(
                        week.StartDate.ToDateTime(new TimeOnly(14, 0)), TimeSpan.Zero),
                });
                await db.SaveChangesAsync();
                subsessions++;

                var carOffset = SyntheticLaps.GetCarOffset(carId);
                var driverLaps = driverSkill
                    .Select(kvp => (
                        CustId: kvp.Key,
                        LapSeconds: SyntheticLaps.GenerateLapTime(
                            kvp.Key, carId, week.RaceWeekIndex,
                            baseLap + carOffset, kvp.Value, stdDev)))
                    .OrderBy(x => x.LapSeconds)
                    .ToList();

                for (var pos = 0; pos < driverLaps.Count; pos++)
                {
                    var (custId, lapSeconds) = driverLaps[pos];
                    db.SubsessionResults.Add(new SubsessionResult
                    {
                        SubsessionId = subsessionId,
                        CustId = custId,
                        DisplayName = DriverName(custId),
                        CarId = carId,
                        CarClassId = CiCatalog.CarClassId,
                        BestLapSeconds = lapSeconds,
                        AverageLapSeconds = lapSeconds * 1.01,
                        FinishPosition = pos,
                        FinishPositionInClass = pos,
                        StartingPosition = pos,
                        StartingPositionInClass = pos,
                        Incidents = 0,
                        LapsComplete = 30,
                        LapsLead = pos == 0 ? 30 : 0,
                        ChampPoints = Math.Max(0, 35 - pos),
                        AggregateChampPoints = Math.Max(0, 35 - pos),
                        NewIRating = 1500,
                        OldIRating = 1500,
                        NewCpi = 2.0,
                        OldCpi = 2.0,
                        ReasonOutId = 0,
                        Division = 1,
                        DropRace = false,
                        Interval = pos == 0 ? 0.0 : lapSeconds - driverLaps[0].LapSeconds,
                    });
                }

                await db.SaveChangesAsync();
                results += driverLaps.Count;
            }
        }

        Console.WriteLine($"  Races: {subsessions:N0} subsessions, {results:N0} results written.");
    }

    // Matches Program.cs's DemoDriverName so demo driver/rival names stay consistent.
    private static string DriverName(long custId) => custId switch
    {
        ApexRacers.Core.DemoData.DriverCustId => "Demo Driver",
        ApexRacers.Core.DemoData.RivalCustId  => "Rival Racer",
        _ => $"Driver {custId}",
    };
}
