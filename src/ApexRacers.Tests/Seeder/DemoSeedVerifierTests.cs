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
        db.Seasons.Add(new Season { Id = 6115, SeriesId = 444, Active = true, Year = 2026, Quarter = 2 });
        db.SeasonCarClasses.Add(new SeasonCarClass { SeasonId = 6115, CarClassId = 100 });
        db.Weeks.Add(new Week { Id = Guid.NewGuid(), SeasonId = 6115, WeekNumber = 0, TrackId = 1 });
        db.SeasonCars.Add(new SeasonCar { SeasonId = 6115, CarId = 132 });
        db.Subsessions.Add(new Subsession { Id = -10, SeasonId = 6115, WeekNumber = 0, TrackId = 1 });
        db.SubsessionResults.Add(new SubsessionResult
        {
            SubsessionId = -10,
            CustId = DemoData.DriverCustId,
            CarId = 132,
            BestLapSeconds = 90.0,
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
        await using var db = DbContextFactory.CreateInMemory();
        await SeedHappyPathAsync(db);
        var checks = await DemoSeedVerifier.VerifyDemoAsync(db, Ct);
        Assert.All(checks, c => Assert.True(c.Passed, $"{c.Name}: {c.Detail}"));
    }

    [Fact]
    public async Task MissingKeyFamily_FailsThatCheckByName()
    {
        await using var db = DbContextFactory.CreateInMemory();
        await SeedHappyPathAsync(db);
        db.ExternalDataCaches.RemoveRange(
            db.ExternalDataCaches.Where(c => c.CacheKey.StartsWith("leaderboard:")));
        await db.SaveChangesAsync(Ct);
        var checks = await DemoSeedVerifier.VerifyDemoAsync(db, Ct);
        Assert.Contains(checks, c => c.Name == "leaderboards" && !c.Passed);
    }

    [Fact]
    public async Task NonSentinelExpiry_FailsSentinelCheck()
    {
        await using var db = DbContextFactory.CreateInMemory();
        await SeedHappyPathAsync(db);
        var row = db.ExternalDataCaches.First();
        row.ExpiresAt = DateTimeOffset.UtcNow.AddHours(1);
        await db.SaveChangesAsync(Ct);
        var checks = await DemoSeedVerifier.VerifyDemoAsync(db, Ct);
        Assert.Contains(checks, c => c.Name == "sentinel-expiry" && !c.Passed);
    }

    [Fact]
    public async Task CleanDatabase_PassesTeardown_And_SeededFailsIt()
    {
        await using var clean = DbContextFactory.CreateInMemory();
        var cleanChecks = await DemoSeedVerifier.VerifyTeardownAsync(clean, Ct);
        Assert.All(cleanChecks, c => Assert.True(c.Passed));

        await using var seeded = DbContextFactory.CreateInMemory();
        await SeedHappyPathAsync(seeded);
        var seededChecks = await DemoSeedVerifier.VerifyTeardownAsync(seeded, Ct);
        Assert.Contains(seededChecks, c => !c.Passed);
    }
}
