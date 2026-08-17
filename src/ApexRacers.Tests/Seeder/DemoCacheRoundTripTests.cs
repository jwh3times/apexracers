using ApexRacers.Api.Services;
using ApexRacers.Core;
using ApexRacers.Seeder.Demo;
using ApexRacers.Tests.Helpers;
using Xunit;

namespace ApexRacers.Tests.Seeder;

/// <summary>
/// The test the suite was missing: seed the demo surface, then read it back through the **real**
/// services with **no iRacing client at all**.
///
/// Every other seeder test asserts a hardcoded key literal, which is a fourth transcription of the
/// format rather than a check against the code that consumes it — so a typo in a service's key
/// left all of them green while the demo page 503'd. Passing <c>null</c> for the client is what
/// makes these meaningful: a miss cannot silently fall through to a live fetch, it throws
/// <see cref="IRacingNotConfiguredException"/>. A hit proves the seeder and the reader agree on
/// the key <em>and</em> that the payload deserializes as the reader's own DTO.
/// </summary>
public class DemoCacheRoundTripTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>No client: any cache miss throws instead of attempting a live fetch.</summary>
    private static CachedIRacingClient Offline(ApexRacers.Data.AppDbContext db) => new(db, null);

    [Fact]
    public async Task SeededMemberCache_IsReadableByDriverStatsService()
    {
        await using var db = DbContextFactory.Create();
        await new DemoCacheSeeder(db).SeedMembersAsync(Ct);

        var service = new DriverStatsService(Offline(db));

        var progression = await service.GetProgressionAsync(DemoData.DriverCustId, Ct);
        Assert.Equal(DemoData.DriverCustId, progression.CustomerId);
        Assert.NotEmpty(progression.Categories);

        var profile = await service.GetDriverProfileAsync(DemoData.DriverCustId, Ct);
        Assert.Equal("Demo Driver", profile.DisplayName);

        // The rival side of /compare reads the same profile/career families.
        var rival = await service.GetComparisonSideAsync(DemoData.RivalCustId, Ct);
        Assert.NotNull(rival);
    }

    [Fact]
    public async Task SeededActivityCache_IsReadableByItsServices()
    {
        await using var db = DbContextFactory.Create();
        await new DemoCacheSeeder(db).SeedActivityAsync(Ct);

        var awards = await new AchievementsService(Offline(db))
            .GetAchievementsAsync(DemoData.DriverCustId, Ct);
        Assert.NotNull(awards);

        var races = await new RaceHistoryService(Offline(db), db)
            .GetRecentRacesAsync(DemoData.DriverCustId, Ct);
        Assert.NotNull(races);
    }

    [Fact]
    public async Task SeededLeaderboardCache_IsReadableByLeaderboardService()
    {
        await using var db = DbContextFactory.Create();
        await new DemoCacheSeeder(db).SeedLeaderboardsAsync(Ct);

        var service = new LeaderboardService(Offline(db));

        // Categories 1..6 are what the seeder writes.
        var board = await service.GetLeaderboardAsync(5, Ct);
        Assert.NotEmpty(board);
    }

    [Fact]
    public async Task AnUnseededKey_ThrowsRatherThanSilentlyPassing()
    {
        // Guards the guard: if a miss did not throw, every assertion above would pass vacuously
        // against an empty cache.
        await using var db = DbContextFactory.Create();

        await Assert.ThrowsAsync<IRacingNotConfiguredException>(() =>
            new DriverStatsService(Offline(db)).GetProgressionAsync(DemoData.DriverCustId, Ct));
    }
}
