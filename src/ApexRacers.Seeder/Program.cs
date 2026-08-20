using System.Text.Json;
using System.Text.Json.Serialization;
using ApexRacers.Core.Models;
using ApexRacers.Core;
using ApexRacers.Data;
using ApexRacers.Seeder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

// ── Configuration ─────────────────────────────────────────────────────────────
var config = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .Build();

var seedDemo       = args.Contains("--demo");
var ciMode         = args.Contains("--ci");
var verifyDemo     = args.Contains("--verify-demo");
var verifyTeardown = args.Contains("--verify-teardown");

var connectionString =
    config["DATABASE_CONNECTION_STRING"]
    ?? "Host=localhost;Port=5432;Database=apexracers;Username=apexracers;Password=devpassword";

var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseNpgsql(connectionString, o => o.MigrationsHistoryTable("__EFMigrationsHistory", "iracing"))
    .Options;

await using var db = new AppDbContext(options);

Console.WriteLine("ApexRacers Seeder — connecting to database…");

// Ensure the schema exists before seeding (idempotent — the same migrations the API
// applies at startup). Lets the Seeder run against a fresh database in CI before the
// API has ever booted.
await db.Database.MigrateAsync();

// Verify-only modes never seed — they're the mechanical pre-enable / post-purge gate for
// the prod demo rollout (deployTODO.md §14). Run against the target DB after seeding
// (--verify-demo) or after running the demo purge script (--verify-teardown).
if (verifyDemo || verifyTeardown)
{
    var checks = verifyDemo
        ? await ApexRacers.Seeder.Verification.DemoSeedVerifier.VerifyDemoAsync(db, CancellationToken.None)
        : await ApexRacers.Seeder.Verification.DemoSeedVerifier.VerifyTeardownAsync(db, CancellationToken.None);
    return ReportVerification(checks) == 0 ? 0 : 1;
}

if (ciMode)
{
    Console.WriteLine("CI mode (--ci): seeding a fully synthetic catalog (no response objects required)…");
    await new CiCatalogSeeder(db).SeedAsync();

    if (seedDemo)
    {
        Console.WriteLine("\nSeeding synthetic demo dataset (--demo)…");
        await new ApexRacers.Seeder.Demo.DemoCacheSeeder(db).SeedAllAsync(CancellationToken.None);
        Console.WriteLine("Demo dataset seeded (ExternalDataCaches + BoP + weather).");

        Console.WriteLine("\nVerifying demo dataset (--verify-demo)…");
        var demoChecks = await ApexRacers.Seeder.Verification.DemoSeedVerifier.VerifyDemoAsync(db, CancellationToken.None);
        if (ReportVerification(demoChecks) > 0) return 1;
    }

    Console.WriteLine("\nCI seeding complete.");
    return 0;
}

// ── Locate response objects ───────────────────────────────────────────────────
var responseObjectsPath = FindResponseObjectsPath();
Console.WriteLine($"Using response objects at: {responseObjectsPath}");

// ── Parse JSON reference data ─────────────────────────────────────────────────
var jsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    // Response files are .jsonc with a leading comment header describing the
    // endpoint and parameters used to capture them — skip comments when parsing.
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
};

var trackCatalog = JsonSerializer
    .Deserialize<List<TrackApiEntry>>(
        File.ReadAllText(Path.Combine(responseObjectsPath, "track", "get.jsonc")), jsonOptions)!
    .ToDictionary(t => t.TrackId);

var carCatalog = JsonSerializer
    .Deserialize<List<CarApiEntry>>(
        File.ReadAllText(Path.Combine(responseObjectsPath, "car", "get.jsonc")), jsonOptions)!
    .ToDictionary(c => c.CarId);

// Asset files (folder + image names) keyed by id string — used to enrich the catalog.
var trackAssets = JsonSerializer.Deserialize<Dictionary<string, TrackAssetEntry>>(
    File.ReadAllText(Path.Combine(responseObjectsPath, "track", "assets.jsonc")), jsonOptions)!;
var carAssets = JsonSerializer.Deserialize<Dictionary<string, CarAssetEntry>>(
    File.ReadAllText(Path.Combine(responseObjectsPath, "car", "assets.jsonc")), jsonOptions)!;

var carClasses = JsonSerializer
    .Deserialize<List<CarClassApiEntry>>(
        File.ReadAllText(Path.Combine(responseObjectsPath, "carclass", "get.jsonc")), jsonOptions)!;

var seriesCatalog = JsonSerializer
    .Deserialize<List<SeriesApiEntry>>(
        File.ReadAllText(Path.Combine(responseObjectsPath, "series", "get.jsonc")), jsonOptions)!
    .ToDictionary(s => s.SeriesId);

var seasonsCatalog = JsonSerializer
    .Deserialize<List<SeasonApiEntry>>(
        File.ReadAllText(Path.Combine(responseObjectsPath, "series", "seasons.jsonc")), jsonOptions)!
    .ToDictionary(s => s.SeasonId);

// Build car_id → estimated avg speed (mph) from relative_speed.
// Calibrated so GT3 (relative_speed 52) ≈ 90 mph, GTP (≥160) ≈ 110 mph.
var carSpeedMph = BuildCarSpeedLookup(carClasses);

var scheduleFiles = Directory.GetFiles(
    Path.Combine(responseObjectsPath, "series"), "season_schedule-*.jsonc");

var schedules = scheduleFiles
    .Select(f => JsonSerializer.Deserialize<SeasonScheduleResponse>(File.ReadAllText(f), jsonOptions)!)
    .Where(s => s.Schedules.Count > 0)
    .ToList();

Console.WriteLine($"Loaded {trackCatalog.Count:N0} tracks, {carCatalog.Count:N0} cars, {carClasses.Count:N0} car classes, {schedules.Count} schedules.");

// ── Step 1: Seed all tracks from catalog ──────────────────────────────────────
Console.WriteLine("Seeding tracks…");
var existingTrackIds = await db.Tracks.Select(t => t.Id).ToHashSetAsync();

var newTracks = trackCatalog.Values
    .Where(t => !existingTrackIds.Contains(t.TrackId))
    .Select(t =>
    {
        trackAssets.TryGetValue(t.TrackId.ToString(), out var a);
        return new Track
        {
            Id                = t.TrackId,
            Name              = t.TrackName,
            ConfigName        = t.ConfigName ?? "",
            CategoryId        = t.CategoryId,
            Category          = t.Category,
            TrackConfigLength = t.TrackConfigLength,
            IsDirt            = t.IsDirt,
            IsOval            = t.IsOval,
            Location          = t.Location,
            TimeZone          = t.TimeZone,
            Retired           = t.Retired,
            CornersPerLap     = t.CornersPerLap,
            Latitude          = t.Latitude,
            Longitude         = t.Longitude,
            PitRoadSpeedLimit = t.PitRoadSpeedLimit,
            NumberPitstalls   = t.NumberPitstalls,
            NightLighting     = t.NightLighting,
            HasSvgMap         = t.HasSvgMap,
            AssetFolder       = a?.Folder,
            SmallImageFile    = a?.SmallImage,
            LargeImageFile    = a?.LargeImage,
            TrackMapUrl       = string.IsNullOrEmpty(a?.TrackMap) ? null : a.TrackMap,
        };
    })
    .ToList();

db.Tracks.AddRange(newTracks);
await db.SaveChangesAsync();
Console.WriteLine($"  {newTracks.Count:N0} tracks added ({existingTrackIds.Count:N0} already present).");

// ── Step 2: Seed all cars from catalog ───────────────────────────────────────
Console.WriteLine("Seeding cars…");
var existingCarIds = await db.Cars.Select(c => c.Id).ToHashSetAsync();

var newCars = carCatalog.Values
    .Where(c => !existingCarIds.Contains(c.CarId))
    .Select(c =>
    {
        carAssets.TryGetValue(c.CarId.ToString(), out var a);
        return new Car
        {
            Id                   = c.CarId,
            Name                 = c.CarName,
            NameAbbreviated      = c.CarNameAbbreviated,
            Retired              = c.Retired,
            FreeWithSubscription = c.FreeWithSubscription,
            PackageId            = c.PackageId,
            Hp                   = c.Hp,
            CarWeight            = c.CarWeight,
            CarMake              = c.CarMake,
            CarModel             = c.CarModel,
            RainEnabled          = c.RainEnabled,
            CategoriesJson       = SerializeList(c.Categories),
            CarTypesJson         = SerializeList(c.CarTypes?.Select(t => t.CarType)),
            AssetFolder          = a?.Folder,
            SmallImageFile       = a?.SmallImage,
            LargeImageFile       = a?.LargeImage,
            LogoPath             = a?.Logo,
        };
    })
    .ToList();

db.Cars.AddRange(newCars);
await db.SaveChangesAsync();
Console.WriteLine($"  {newCars.Count:N0} cars added ({existingCarIds.Count:N0} already present).");

// ── Step 3: Seed car classes and car-class membership ─────────────────────────
Console.WriteLine("Seeding car classes…");
var existingCarClassIds = await db.CarClasses.Select(c => c.Id).ToHashSetAsync();

var newCarClasses = carClasses
    .Where(c => !existingCarClassIds.Contains(c.CarClassId))
    .Select(c => new CarClass
    {
        Id            = c.CarClassId,
        Name          = c.Name,
        ShortName     = c.ShortName,
        RelativeSpeed = c.RelativeSpeed,
    })
    .ToList();

db.CarClasses.AddRange(newCarClasses);
await db.SaveChangesAsync();
Console.WriteLine($"  {newCarClasses.Count:N0} car classes added ({existingCarClassIds.Count:N0} already present).");

var existingCarClassCarKeys = await db.CarClassCars
    .Select(cc => new { cc.CarClassId, cc.CarId })
    .ToHashSetAsync();

var newCarClassCars = carClasses
    .SelectMany(c => c.CarsInClass.Select(m => new CarClassCar
    {
        CarClassId = c.CarClassId,
        CarId      = m.CarId,
    }))
    .Where(cc => !existingCarClassCarKeys.Contains(new { cc.CarClassId, cc.CarId })
                 && existingCarIds.Union(newCars.Select(c => c.Id)).Contains(cc.CarId))
    .ToList();

db.CarClassCars.AddRange(newCarClassCars);
await db.SaveChangesAsync();
Console.WriteLine($"  {newCarClassCars.Count:N0} car-class memberships added.");

// ── Step 4: Seed series, seasons, weeks, season-cars from schedules ───────────
Console.WriteLine("Seeding series / seasons / weeks…");

foreach (var schedule in schedules)
{
    var firstWeek = schedule.Schedules[0];
    var (year, quarter) = ParseSeasonYearQuarter(firstWeek.SeasonName);

    Console.WriteLine($"  {firstWeek.SeriesName} ({firstWeek.SeasonId}) — {year} S{quarter}");

    // Series
    seriesCatalog.TryGetValue(firstWeek.SeriesId, out var seriesEntry);
    // Derive official from the first matching season entry
    var seriesOfficial = seasonsCatalog.Values
        .FirstOrDefault(s => s.SeriesId == firstWeek.SeriesId)?.Official;
    var minLicenseGroup = seriesEntry?.AllowedLicenses.Count > 0
        ? seriesEntry.AllowedLicenses.Min(l => l.LicenseGroup)
        : (int?)null;

    var series = await db.Series.FindAsync(firstWeek.SeriesId);
    if (series is null)
    {
        db.Series.Add(new Series
        {
            Id           = firstWeek.SeriesId,
            Name         = firstWeek.SeriesName,
            CategoryId   = seriesEntry?.CategoryId,
            Category     = seriesEntry?.Category,
            LicenseGroup = minLicenseGroup,
            Official     = seriesOfficial,
        });
    }
    else
    {
        series.Name         = firstWeek.SeriesName;
        series.CategoryId   ??= seriesEntry?.CategoryId;
        series.Category     ??= seriesEntry?.Category;
        series.LicenseGroup ??= minLicenseGroup;
        series.Official     ??= seriesOfficial;
    }

    // Season
    seasonsCatalog.TryGetValue(firstWeek.SeasonId, out var seasonEntry);

    var season = await db.Seasons.FindAsync(firstWeek.SeasonId);
    if (season is null)
    {
        db.Seasons.Add(new Season
        {
            Id           = firstWeek.SeasonId,
            SeriesId     = firstWeek.SeriesId,
            Year         = year,
            Quarter      = quarter,
            Active       = true,
            LicenseGroup = seasonEntry?.LicenseGroup,
            Official     = seasonEntry?.Official,
            Drops        = seasonEntry?.Drops,
            FixedSetup   = seasonEntry?.FixedSetup,
            Multiclass   = seasonEntry?.Multiclass,
        });
    }
    else
    {
        season.Active       = true;
        season.LicenseGroup ??= seasonEntry?.LicenseGroup;
        season.Official     ??= seasonEntry?.Official;
        season.Drops        ??= seasonEntry?.Drops;
        season.FixedSetup   ??= seasonEntry?.FixedSetup;
        season.Multiclass   ??= seasonEntry?.Multiclass;
    }

    await db.SaveChangesAsync();

    // SeasonCarClass
    if (seasonEntry is not null)
    {
        foreach (var carClassId in seasonEntry.CarClassIds)
        {
            if (await db.SeasonCarClasses.FindAsync(firstWeek.SeasonId, carClassId) is null
                && await db.CarClasses.FindAsync(carClassId) is not null)
            {
                db.SeasonCarClasses.Add(new SeasonCarClass
                {
                    SeasonId   = firstWeek.SeasonId,
                    CarClassId = carClassId,
                });
            }
        }
        await db.SaveChangesAsync();
    }

    // Collect distinct cars across all weeks
    var allCarIds = schedule.Schedules
        .SelectMany(w => w.CarRestrictions.Select(c => c.CarId))
        .Distinct()
        .ToList();

    foreach (var carId in allCarIds)
    {
        if (await db.SeasonCars.FindAsync(firstWeek.SeasonId, carId) is null)
            db.SeasonCars.Add(new SeasonCar { SeasonId = firstWeek.SeasonId, CarId = carId });
    }

    // Weeks
    foreach (var week in schedule.Schedules)
    {
        var startDate = DateOnly.Parse(week.StartDate);

        var existing = await db.Weeks.FirstOrDefaultAsync(
            w => w.SeasonId == week.SeasonId && w.WeekNumber == week.RaceWeekNum);

        if (existing is null)
            db.Weeks.Add(new Week
            {
                SeasonId   = week.SeasonId,
                WeekNumber = week.RaceWeekNum,
                TrackId    = week.Track.TrackId,
                StartDate  = startDate,
            });
        else
        {
            existing.TrackId   = week.Track.TrackId;
            existing.StartDate = startDate;
        }
    }

    await db.SaveChangesAsync();
}

// ── Step 5: Synthetic driver pool ─────────────────────────────────────────────
const int DriverStart = 100_001;
const int DriverCount = 200;

var driverSkillFactors = Enumerable.Range(0, DriverCount)
    .ToDictionary(i => (long)(DriverStart + i), i => SyntheticLaps.ComputeSkillFactor(DriverStart + i));

// ── Step 6: Seed subsessions and results ─────────────────────────────────────
Console.WriteLine("Seeding subsessions and race results…");
int totalSubsessions = 0;
int totalResults = 0;

// Load all car classes for CarClassId lookup.
// Exclude class 0 ("Hosted All Cars Class") — it contains every car and causes
// the carClassId == 0 guard below to skip cars that have a real class as well.
var carClassByCar = carClasses
    .Where(cc => cc.CarClassId != 0)
    .SelectMany(cc => cc.CarsInClass.Select(m => (m.CarId, cc.CarClassId)))
    .GroupBy(x => x.CarId)
    .ToDictionary(g => g.Key, g => g.First().CarClassId);

// Ensure all car classes exist in DB (already seeded in step 3, but use their IDs)
var dbCarClassIds = await db.CarClasses.Select(c => c.Id).ToHashSetAsync();

foreach (var schedule in schedules)
{
    var firstWeek = schedule.Schedules[0];
    var avgSpeedMph = GetAvgSpeedMph(firstWeek.SeasonId);

    var weekRows = await db.Weeks
        .Where(w => w.SeasonId == firstWeek.SeasonId)
        .ToListAsync();
    var weekByNumber = weekRows.ToDictionary(w => w.WeekNumber);

    foreach (var week in schedule.Schedules)
    {
        if (!weekByNumber.TryGetValue(week.RaceWeekNum, out var weekRow))
            continue;

        var carIds = week.CarRestrictions.Select(c => c.CarId).ToList();
        if (carIds.Count == 0) continue;

        var trackLength = trackCatalog.TryGetValue(week.Track.TrackId, out var t)
            ? t.TrackConfigLength ?? 2.5
            : 2.5;

        // One synthetic subsession per car per week. Each driver races one car
        // so the (SubsessionId, CustId) PK is never violated.
        // ID formula: -(seasonId * 10000 + weekNum * 100 + carIndex)
        for (int carIndex = 0; carIndex < carIds.Count; carIndex++)
        {
            var carId     = carIds[carIndex];
            var mph       = carSpeedMph.TryGetValue(carId, out var s) ? s : avgSpeedMph;
            var baseLap   = trackLength / mph * 3600.0;
            var stdDev    = Math.Max(1.0, baseLap * 0.02);
            var carOffset = SyntheticLaps.GetCarOffset(carId);

            carClassByCar.TryGetValue(carId, out var carClassId);
            if (carClassId == 0 || !dbCarClassIds.Contains(carClassId)) continue;

            var subsessionId = -(firstWeek.SeasonId * 10000 + week.RaceWeekNum * 100 + carIndex);

            bool alreadySeeded = await db.Subsessions.AnyAsync(s => s.Id == subsessionId);
            if (alreadySeeded) continue;

            db.Subsessions.Add(new ApexRacers.Core.Models.Subsession
            {
                Id                   = subsessionId,
                SeasonId             = firstWeek.SeasonId,
                WeekNumber           = week.RaceWeekNum,
                WeekId               = weekRow.Id,
                TrackId              = week.Track.TrackId,
                OfficialSession      = true,
                EventStrengthOfField = 1500,
                // The sole Split of its Race Session, so index 0 of a count of 1.
                SplitIndex           = 0,
                SplitCount           = 1,
                StartTime            = DateTimeOffset.Parse(week.StartDate + "T14:00:00Z"),
            });
            await db.SaveChangesAsync();
            totalSubsessions++;

            var driverLaps = driverSkillFactors
                .Select(kvp => (
                    CustId: kvp.Key,
                    LapSeconds: SyntheticLaps.GenerateLapTime(
                        kvp.Key, carId, week.RaceWeekNum,
                        baseLap + carOffset, kvp.Value, stdDev)))
                .OrderBy(x => x.LapSeconds)
                .ToList();

            for (int pos = 0; pos < driverLaps.Count; pos++)
            {
                var (custId, lapSeconds) = driverLaps[pos];
                db.SubsessionResults.Add(new ApexRacers.Core.Models.SubsessionResult
                {
                    SubsessionId            = subsessionId,
                    CustId                  = custId,
                    DisplayName             = DemoDriverName(custId),
                    CarId                   = carId,
                    CarClassId              = carClassId,
                    BestLapSeconds          = lapSeconds,
                    AverageLapSeconds       = lapSeconds * 1.01,
                    FinishPosition          = pos,
                    FinishPositionInClass   = pos,
                    StartingPosition        = pos,
                    StartingPositionInClass = pos,
                    Incidents               = 0,
                    LapsComplete            = 30,
                    LapsLead                = pos == 0 ? 30 : 0,
                    ChampPoints             = Math.Max(0, 35 - pos),
                    AggregateChampPoints    = Math.Max(0, 35 - pos),
                    NewIRating              = 1500,
                    OldIRating              = 1500,
                    NewCpi                  = 2.0,
                    OldCpi                  = 2.0,
                    ReasonOutId             = 0,
                    Division                = 1,
                    DropRace                = false,
                    Interval                = pos == 0 ? 0.0 : lapSeconds - driverLaps[0].LapSeconds,
                });
            }

            await db.SaveChangesAsync();
            totalResults += driverLaps.Count;
        }
    }

    Console.WriteLine($"  {firstWeek.SeriesName}: done");
}

Console.WriteLine($"\nSubsession seeding complete — {totalSubsessions:N0} subsessions, {totalResults:N0} race results written.");

// ── Step 7: Seed CarPercentileResult for ApplicationUsers ─────────────────
Console.WriteLine("Seeding percentile snapshots for application users…");

var appUsers = await db.Users
    .Where(u => u.IRacingCustomerId != null)
    .ToListAsync();

if (appUsers.Count == 0)
{
    Console.WriteLine("  No application users with iRacing Customer IDs found — skipping.");
}
else
{
    var custIds = appUsers.Select(u => u.IRacingCustomerId!.Value).ToList();

    // Best lap per (custId, carId, seriesId, weekId) across all seeded results
    var userResults = await db.SubsessionResults
        .Where(r => custIds.Contains(r.CustId)
                 && r.BestLapSeconds > 0
                 && r.Subsession.WeekId.HasValue)
        .Select(r => new
        {
            r.CustId,
            r.CarId,
            r.BestLapSeconds,
            WeekId   = r.Subsession.WeekId!.Value,
            SeriesId = r.Subsession.Season.SeriesId,
        })
        .ToListAsync();

    if (userResults.Count == 0)
    {
        Console.WriteLine("  No matching race results found for application users — skipping.");
    }
    else
    {
        var userBestByGroup = userResults
            .GroupBy(r => (r.CustId, r.CarId, r.SeriesId, r.WeekId))
            .Select(g => new
            {
                g.Key.CustId,
                g.Key.CarId,
                g.Key.SeriesId,
                g.Key.WeekId,
                BestLap = g.Min(r => r.BestLapSeconds),
            })
            .ToList();

        var relevantWeekIds = userBestByGroup.Select(r => r.WeekId).Distinct().ToList();
        var relevantCarIds  = userBestByGroup.Select(r => r.CarId).Distinct().ToList();

        // Back-date each percentile snapshot to the last day of its race week so
        // the analytics trend chart shows a meaningful time axis instead of all
        // rows sharing today's date.
        var weekStartDates = await db.Weeks
            .Where(w => relevantWeekIds.Contains(w.Id))
            .Select(w => new { w.Id, w.StartDate })
            .ToDictionaryAsync(w => w.Id, w => w.StartDate);

        // Full field: best lap per (custId, carId, weekId) across all drivers in those weeks
        var fieldResults = await db.SubsessionResults
            .Where(r => r.Subsession.WeekId.HasValue
                     && relevantWeekIds.Contains(r.Subsession.WeekId!.Value)
                     && relevantCarIds.Contains(r.CarId)
                     && r.BestLapSeconds > 0)
            .GroupBy(r => new { r.CustId, r.CarId, WeekId = r.Subsession.WeekId!.Value })
            .Select(g => new { g.Key.CustId, g.Key.CarId, g.Key.WeekId, BestLap = g.Min(r => r.BestLapSeconds) })
            .ToListAsync();

        // Grouped with CustId retained so each driver can be ranked against the *others* —
        // see FieldPercentile.Rank.
        var fieldByCarWeek = fieldResults
            .GroupBy(r => (r.CarId, r.WeekId))
            .ToDictionary(g => g.Key, g => g.ToList());

        // Load existing CarPercentileResult rows for upsert
        var userIds = appUsers.Select(u => u.Id).ToList();
        var existingRows = await db.CarPercentileResults
            .Where(r => userIds.Contains(r.UserId))
            .ToListAsync();

        var existingLookup = existingRows
            .ToDictionary(r => (r.UserId, r.CarId, r.SeriesId, r.WeekId));

        var custIdToUser = appUsers.ToDictionary(u => u.IRacingCustomerId!.Value);
        int written      = 0;

        foreach (var entry in userBestByGroup)
        {
            if (!custIdToUser.TryGetValue(entry.CustId, out var appUser)) continue;
            if (!fieldByCarWeek.TryGetValue((entry.CarId, entry.WeekId), out var fieldRows)) continue;

            var otherLaps      = fieldRows.Where(r => r.CustId != entry.CustId).Select(r => r.BestLap).ToList();
            var total          = FieldPercentile.FieldSize(otherLaps);
            var percentileRank = FieldPercentile.Rank(entry.BestLap, otherLaps);
            var topShare       = FieldPercentile.TopSharePercent(entry.BestLap, otherLaps);

            // Simulate the timestamp as the final day of the race week at 20:00 UTC.
            var weekStart  = weekStartDates.TryGetValue(entry.WeekId, out var sd) ? sd : DateOnly.FromDateTime(DateTime.UtcNow);
            var computedAt = new DateTimeOffset(weekStart.AddDays(6).ToDateTime(new TimeOnly(20, 0)), TimeSpan.Zero);

            var key = (appUser.Id, entry.CarId, entry.SeriesId, entry.WeekId);
            if (existingLookup.TryGetValue(key, out var existing))
            {
                existing.PercentileRank  = percentileRank;
                existing.TopSharePercent = topShare;
                existing.SampleSize      = total;
                existing.ComputedAt      = computedAt;
            }
            else
            {
                var newRow = new CarPercentileResult
                {
                    UserId         = appUser.Id,
                    CarId          = entry.CarId,
                    SeriesId       = entry.SeriesId,
                    WeekId         = entry.WeekId,
                    PercentileRank  = percentileRank,
                    TopSharePercent = topShare,
                    SampleSize      = total,
                    ComputedAt      = computedAt,
                };
                db.CarPercentileResults.Add(newRow);
                existingLookup[key] = newRow;
            }
            written++;
        }

        await db.SaveChangesAsync();
        Console.WriteLine($"  {written:N0} percentile snapshots written for {appUsers.Count} user(s).");
    }
}

Console.WriteLine("\nSeeding complete.");

if (seedDemo)
{
    Console.WriteLine("\nSeeding synthetic demo dataset (--demo)…");
    await new ApexRacers.Seeder.Demo.DemoCacheSeeder(db).SeedAllAsync(CancellationToken.None);
    Console.WriteLine("Demo dataset seeded (ExternalDataCaches + BoP + weather).");

    Console.WriteLine("\nVerifying demo dataset (--verify-demo)…");
    var demoChecks = await ApexRacers.Seeder.Verification.DemoSeedVerifier.VerifyDemoAsync(db, CancellationToken.None);
    if (ReportVerification(demoChecks) > 0) return 1;
}

return 0;

// ── Helpers ───────────────────────────────────────────────────────────────────

// Prints one line per verification check and a PASS/FAIL summary; returns the failed count.
static int ReportVerification(List<ApexRacers.Seeder.Verification.VerificationCheck> checks)
{
    var failed = 0;
    foreach (var c in checks)
    {
        Console.WriteLine($"  [{(c.Passed ? "PASS" : "FAIL")}] {c.Name} — {c.Detail}");
        if (!c.Passed) failed++;
    }
    Console.WriteLine(failed == 0 ? "\nVerification PASSED." : $"\nVerification FAILED ({failed} check(s)).");
    return failed;
}

// Serialize a slug list to a JSON array string for the catalog's CategoriesJson/CarTypesJson
// columns; null when empty (mirrors CatalogIngest in the ingestion worker).
static string? SerializeList(IEnumerable<string?>? items)
{
    var list = items?.Where(s => !string.IsNullOrEmpty(s)).ToList();
    return list is { Count: > 0 } ? JsonSerializer.Serialize(list) : null;
}

static string FindResponseObjectsPath()
{
    // Local-only files were relocated under private/; check both the legacy
    // repo-root location and private/ so existing setups keep working.
    string[] relativeCandidates =
    {
        "iracing-api-response-objects",
        Path.Combine("private", "iracing-api-response-objects"),
    };

    foreach (var rel in relativeCandidates)
    {
        var candidate = Path.Combine(Environment.CurrentDirectory, rel);
        if (Directory.Exists(candidate)) return candidate;
    }

    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null)
    {
        foreach (var rel in relativeCandidates)
        {
            var p = Path.Combine(dir.FullName, rel);
            if (Directory.Exists(p)) return p;
        }
        dir = dir.Parent;
    }

    throw new DirectoryNotFoundException(
        "Cannot find iracing-api-response-objects (checked repo root and private/). "
        + "Populate it under private/ and run the seeder from the repo root.");
}

// Fallback average speed (mph) per season when a car has no class entry.
static double GetAvgSpeedMph(int seasonId) => seasonId switch
{
    6115 => 90.0,  // GT3 Challenge Fixed
    6099 => 83.0,  // GT4 Challenge
    6091 => 99.0,  // Ring Meister (Ligier LMP3)
    6124 => 90.0,  // IMSA (GT3 baseline; GTP cars get their own speed from carSpeedMph)
    _    => 88.0,
};

// Build a car_id → avg-speed-mph lookup from the car class catalog.
// relative_speed is an arbitrary iRacing ranking, not mph — calibrated here
// so GT3 (rel 52) ≈ 90 mph and GTP (rel ≥160) ≈ 110 mph.
static Dictionary<int, double> BuildCarSpeedLookup(IReadOnlyList<CarClassApiEntry> carClasses)
{
    var lookup = new Dictionary<int, double>();
    foreach (var cls in carClasses)
    {
        double mph = cls.RelativeSpeed switch
        {
            >= 160 => 110.0,  // GTP, Indy, modern F1
            >= 120 => 105.0,  // LMP1, LMP2
            >= 80  => 100.0,  // older prototypes, Riley DP
            >= 65  => 95.0,   // LMP3, GTE
            >= 48  => 90.0,   // GT3
            >= 38  => 83.0,   // GT4, slower sports cars
            _      => 75.0,
        };
        foreach (var car in cls.CarsInClass)
            lookup.TryAdd(car.CarId, mph);
    }
    return lookup;
}

static (int Year, int Quarter) ParseSeasonYearQuarter(string seasonName)
{
    // "GT3 Challenge Fixed by Fanatec - 2026 Season 2"
    var dash = seasonName.LastIndexOf(" - ", StringComparison.Ordinal);
    if (dash >= 0)
    {
        var tail = seasonName[(dash + 3)..].Trim(); // "2026 Season 2"
        var parts = tail.Split(' ');
        if (parts.Length >= 3 &&
            int.TryParse(parts[0], out var year) &&
            int.TryParse(parts[2], out var quarter))
            return (year, quarter);
    }
    return (DateTimeOffset.UtcNow.Year, 1);
}

// Display name for a synthetic driver — matches the demo cache builders (DemoMemberData)
// so the demo driver/rival show consistent names on Race Detail + /compare suggestions.
static string DemoDriverName(long custId) => custId switch
{
    ApexRacers.Core.DemoData.DriverCustId => "Demo Driver",
    ApexRacers.Core.DemoData.RivalCustId  => "Rival Racer",
    _ => $"Driver {custId}",
};
