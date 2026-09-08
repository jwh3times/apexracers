using ApexRacers.Api.Services;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Seeder.Demo;
using ApexRacers.Seeder.Verification;
using ApexRacers.Tests.Helpers;
using Xunit;

namespace ApexRacers.Tests.Seeder;

public class DemoSeedVerifierTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Minimal "fully seeded" fixture: one active season/class/week/car, one negative
    // subsession with a demo-driver result, an "iracing-demo" flag row, and — via the
    // real DemoCacheSeeder.SeedAllAsync — one cache row per required key family under
    // the seeder's exact runtime key formats (so this stays in lockstep by construction).
    private static async Task SeedHappyPathAsync(AppDbContext db)
    {
        db.Series.Add(new Series { Id = 444, Name = "GT3 Cup" });
        db.Seasons.Add(new Season { Id = 6115, SeriesId = 444, Active = true, Year = 2026, Quarter = 2 });
        db.CarClasses.Add(new CarClass { Id = 100, Name = "GT3", ShortName = "GT3" });
        db.Cars.Add(new Car { Id = 132, Name = "Demo GT3", NameAbbreviated = "GT3" });
        db.Tracks.Add(new Track { Id = 1, Name = "Demo Raceway" });
        db.SeasonCarClasses.Add(new SeasonCarClass { SeasonId = 6115, CarClassId = 100 });
        var weekId = Guid.NewGuid();
        db.Weeks.Add(new Week { Id = weekId, SeasonId = 6115, RaceWeekIndex = 0, TrackId = 1 });
        db.SeasonCars.Add(new SeasonCar { SeasonId = 6115, CarId = 132 });
        db.Subsessions.Add(new Subsession
        {
            Id = -10,
            SeasonId = 6115,
            RaceWeekIndex = 0,
            WeekId = weekId,
            TrackId = 1,
            StartTime = DateTimeOffset.Parse("2026-06-01T14:00:00Z"),
            EventStrengthOfField = 1500,
        });
        db.SubsessionResults.Add(new SubsessionResult
        {
            SubsessionId = -10,
            CustId = DemoData.DriverCustId,
            CarId = 132,
            BestLapSeconds = 90.0,
            DisplayName = "Demo Driver",
        });
        db.FeatureFlags.Add(new FeatureFlag
        {
            Key = "iracing-demo",
            Name = "iRacing demo data",
            IsEnabled = false,
            MinimumRole = "Alpha",
        });
        await db.SaveChangesAsync(Ct);

        await new DemoCacheSeeder(db).SeedAllAsync(Ct);
    }

    [Fact]
    public async Task FullySeeded_Passes()
    {
        await using var db = DbContextFactory.Create();
        await SeedHappyPathAsync(db);
        var checks = await DemoSeedVerifier.VerifyDemoAsync(db, Ct);
        Assert.All(checks, c => Assert.True(c.Passed, $"{c.Name}: {c.Detail}"));
    }

    [Fact]
    public async Task MissingKeyFamily_FailsThatCheckByName()
    {
        await using var db = DbContextFactory.Create();
        await SeedHappyPathAsync(db);
        db.ExternalDataCaches.RemoveRange(
            db.ExternalDataCaches.Where(c => c.CacheKey.StartsWith("leaderboard:")));
        await db.SaveChangesAsync(Ct);
        var checks = await DemoSeedVerifier.VerifyDemoAsync(db, Ct);
        Assert.Contains(checks, c => c.Name == "leaderboards" && !c.Passed);
    }

    [Fact]
    public async Task OrphanedRecentRace_FailsRecentRaceLinksCheck()
    {
        await using var db = DbContextFactory.Create();
        await SeedHappyPathAsync(db);
        await DemoCache.UpsertAsync(
            db,
            IRacingCacheKeys.RecentRaces(DemoData.DriverCustId).Key,
            new List<RecentRaceCacheRow>
            {
                new(
                    -999,
                    DateTimeOffset.Parse("2026-06-01T14:00:00Z"),
                    "GT3 Cup",
                    1,
                    "Demo Raceway",
                    132,
                    0,
                    0,
                    0,
                    0,
                    0,
                    1500,
                    0),
            },
            Ct);

        var checks = await DemoSeedVerifier.VerifyDemoAsync(db, Ct);

        var check = Assert.Single(checks, c => c.Name == "recent-race-links");
        Assert.False(check.Passed);
        Assert.Contains("-999", check.Detail);
    }

    [Fact]
    public async Task EmptyRecentRacePayload_FailsRecentRaceLinksCheck()
    {
        await using var db = DbContextFactory.Create();
        await SeedHappyPathAsync(db);
        await DemoCache.UpsertAsync(
            db,
            IRacingCacheKeys.RecentRaces(DemoData.DriverCustId).Key,
            new List<RecentRaceCacheRow>(),
            Ct);

        var checks = await DemoSeedVerifier.VerifyDemoAsync(db, Ct);

        var check = Assert.Single(checks, c => c.Name == "recent-race-links");
        Assert.False(check.Passed);
        Assert.Contains("no recent races", check.Detail);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("[null]")]
    public async Task MalformedRecentRacePayload_FailsRecentRaceLinksCheck(string payload)
    {
        await using var db = DbContextFactory.Create();
        await SeedHappyPathAsync(db);
        var cache = db.ExternalDataCaches.Single(
            c => c.CacheKey == IRacingCacheKeys.RecentRaces(DemoData.DriverCustId).Key);
        cache.Payload = payload;
        await db.SaveChangesAsync(Ct);

        var checks = await DemoSeedVerifier.VerifyDemoAsync(db, Ct);

        var check = Assert.Single(checks, c => c.Name == "recent-race-links");
        Assert.False(check.Passed);
        Assert.Contains("malformed", check.Detail);
    }

    [Fact]
    public async Task MissingRecentRacePayload_FailsRecentRaceLinksCheck()
    {
        await using var db = DbContextFactory.Create();
        await SeedHappyPathAsync(db);
        db.ExternalDataCaches.Remove(
            db.ExternalDataCaches.Single(
                c => c.CacheKey == IRacingCacheKeys.RecentRaces(DemoData.DriverCustId).Key));
        await db.SaveChangesAsync(Ct);

        var checks = await DemoSeedVerifier.VerifyDemoAsync(db, Ct);

        var check = Assert.Single(checks, c => c.Name == "recent-race-links");
        Assert.False(check.Passed);
        Assert.Contains("missing", check.Detail);
    }

    [Fact]
    public async Task SentinelChecks_UseTheOwnedThresholdAsAnInclusiveRange()
    {
        await using var db = DbContextFactory.Create();
        await SeedHappyPathAsync(db);
        var below = db.ExternalDataCaches.First();
        below.ExpiresAt = DemoCache.SentinelThreshold.AddTicks(-1);
        await db.SaveChangesAsync(Ct);

        var belowChecks = await DemoSeedVerifier.VerifyDemoAsync(db, Ct);
        Assert.Contains(belowChecks, check =>
            check.Name == "sentinel-expiry" && !check.Passed);

        below.ExpiresAt = DemoCache.SentinelThreshold;
        await db.SaveChangesAsync(Ct);

        var inclusiveChecks = await DemoSeedVerifier.VerifyDemoAsync(db, Ct);
        Assert.Contains(inclusiveChecks, check =>
            check.Name == "sentinel-expiry" && check.Passed);

        var teardownChecks = await DemoSeedVerifier.VerifyTeardownAsync(db, Ct);
        var sentinelCheck = Assert.Single(
            teardownChecks, check => check.Name == "no-sentinel-cache");
        Assert.False(sentinelCheck.Passed);
        Assert.Equal($"{db.ExternalDataCaches.Count()} sentinel rows remain", sentinelCheck.Detail);
    }

    [Fact]
    public async Task ZeroBestLapDemoResult_DoesNotProduceSpuriousLapDataFailure()
    {
        await using var db = DbContextFactory.Create();
        await SeedHappyPathAsync(db);

        // A second negative subsession for the demo driver with no valid best lap (e.g. a DNF).
        // DemoCacheSeeder.SeedLapDataAsync skips these (its own BestLapSeconds > 0 filter), so it
        // never writes a "laps:" cache row for -11. The verifier's expected-key derivation must
        // apply the same filter or it will demand a key that was never seeded and spuriously fail.
        db.Subsessions.Add(new Subsession { Id = -11, SeasonId = 6115, RaceWeekIndex = 0, TrackId = 1 });
        db.SubsessionResults.Add(new SubsessionResult
        {
            SubsessionId = -11,
            CustId = DemoData.DriverCustId,
            CarId = 132,
            BestLapSeconds = 0,
        });
        await db.SaveChangesAsync(Ct);

        var checks = await DemoSeedVerifier.VerifyDemoAsync(db, Ct);
        Assert.Contains(checks, c => c.Name == "lap-data" && c.Passed);
    }

    [Fact]
    public async Task CleanDatabase_PassesTeardown_And_SeededFailsIt()
    {
        await using var clean = DbContextFactory.Create();
        var cleanChecks = await DemoSeedVerifier.VerifyTeardownAsync(clean, Ct);
        Assert.All(cleanChecks, c => Assert.True(c.Passed));

        await using var seeded = DbContextFactory.Create();
        await SeedHappyPathAsync(seeded);
        var seededChecks = await DemoSeedVerifier.VerifyTeardownAsync(seeded, Ct);
        Assert.Contains(seededChecks, c => !c.Passed);
    }
}
