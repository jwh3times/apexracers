using ApexRacers.Api.Services;
using ApexRacers.Api.Dtos;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Tests.Helpers;
using ApexRacers.Seeder.Demo;
using Aydsko.iRacingData;
using NSubstitute;
using Xunit;

namespace ApexRacers.Tests.Services;

public class DemoAcquisitionTests
{
    private sealed record Evidence(int Lap);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RealMappedArraysRemainTypedAndNameFreeOnFreshAndWarmReads()
    {
        await using var db = DbContextFactory.Create();
        var cache = new MappingEvidenceCache(db, Substitute.For<IDataClient>());
        var spec = IRacingCacheKeys.DriverSearch("fixture")!.Value;
        var fresh = await cache.GetOrFetchAsync(spec,
            _ => Task.FromResult(new[] { new DriverSearchResultDto(100001, "Upstream Private Name") }), Ct);
        Assert.Equal(string.Empty, Assert.Single(fresh).DriverName);
        var warm = await cache.GetOrFetchAsync<DriverSearchResultDto[]>(spec,
            _ => throw new InvalidOperationException("Warm cache should not fetch"), Ct);
        Assert.Equal(string.Empty, Assert.Single(warm).DriverName);
    }

    [Fact]
    public async Task RawSdkCollectionsAreRejectedBeforeProviderAcquisition()
    {
        await using var db = DbContextFactory.Create();
        var cache = new CachedIRacingClient(db, Substitute.For<IDataClient>());
        var providerReached = false;
        await Assert.ThrowsAsync<ArgumentException>(() => cache.GetOrFetchAsync(
            IRacingCacheKeys.Profile(100001), _ =>
            {
                providerReached = true;
                return Task.FromResult(new List<Aydsko.iRacingData.Member.MemberProfile> { new() });
            }, Ct));
        Assert.False(providerReached);
        Assert.Empty(db.ExternalDataCaches);
    }

    [Theory]
    [InlineData("leaderboard")]
    [InlineData("tt")]
    [InlineData("qualifying")]
    [InlineData("search")]
    public async Task RemainingMappedDriverContractsAreNameFree(string family)
    {
        await using var db = DbContextFactory.Create();
        var cache = new MappingEvidenceCache(db, Substitute.For<IDataClient>());
        object result = family switch
        {
            "leaderboard" => await cache.GetOrFetchAsync<IReadOnlyList<GlobalLeaderboardEntryDto>>(
                IRacingCacheKeys.Leaderboard(5), _ => Task.FromResult<IReadOnlyList<GlobalLeaderboardEntryDto>>(
                    [new(5, 1, 100001, "Upstream Private Name", "US", 5, 1, 2400, 2000, 100)]), Ct),
            "tt" => await cache.GetOrFetchAsync<IReadOnlyList<SeasonTtStandingDto>>(
                IRacingCacheKeys.TimeTrialStandings(7, 1), _ => Task.FromResult<IReadOnlyList<SeasonTtStandingDto>>(
                    [new(1, 100001, "Upstream Private Name", 1, 1500, 5, 1, 2, 0, 42, 3.5, 2)]), Ct),
            "qualifying" => await cache.GetOrFetchAsync<IReadOnlyList<SeasonQualifyResultDto>>(
                IRacingCacheKeys.QualifyResults(7, 1, 0), _ => Task.FromResult<IReadOnlyList<SeasonQualifyResultDto>>(
                    [new(1, 100001, "Upstream Private Name", 1, 2400, 91, 0)]), Ct),
            _ => await cache.GetOrFetchAsync(
                IRacingCacheKeys.DriverSearch("fixture")!.Value,
                _ => Task.FromResult(new List<DriverSearchResultDto> { new(100001, "Upstream Private Name") }), Ct),
        };
        Assert.DoesNotContain("Upstream Private Name", System.Text.Json.JsonSerializer.Serialize(result));
        Assert.DoesNotContain("Upstream Private Name", Assert.Single(db.ExternalDataCaches).Payload);
    }

    [Fact]
    public async Task RealStandingEvidenceRetainsResultsWithoutProviderNames()
    {
        await using var db = DbContextFactory.Create();
        var cache = new MappingEvidenceCache(db, Substitute.For<IDataClient>());
        var result = await cache.GetOrFetchAsync<IReadOnlyList<SeasonStandingDto>>(
            IRacingCacheKeys.Standings(7, 1), _ => Task.FromResult<IReadOnlyList<SeasonStandingDto>>(
                [new(3, 100001, "Upstream Private Name", 1, 5, 1, 2, 0, 42, 3.5, 2)]), Ct);
        var row = Assert.Single(result);
        Assert.Equal(string.Empty, row.DriverName);
        Assert.Equal(42, row.Points);
        Assert.Equal(3, row.Standing);
        Assert.DoesNotContain("Upstream Private Name", Assert.Single(db.ExternalDataCaches).Payload);
    }

    [Fact]
    public async Task RealMappedProfileCannotPersistOrReturnTheProviderName()
    {
        await using var db = DbContextFactory.Create();
        var cache = new MappingEvidenceCache(db, Substitute.For<IDataClient>());
        var result = await cache.GetOrFetchAsync(IRacingCacheKeys.Profile(100001),
            _ => Task.FromResult(new ProfileSnapshot("Upstream Private Name", null, null, null, [])), Ct);
        Assert.Equal(string.Empty, result.DisplayName);
        Assert.DoesNotContain("Upstream Private Name", Assert.Single(db.ExternalDataCaches).Payload);
    }

    [Fact]
    public async Task PersistedDemoRaceEvidenceIsUnavailableToRealQueries()
    {
        await using var shared = DbContextFactory.CreateShared();
        await using var demo = shared.NewContext(DataProvenance.Demo);
        demo.SubsessionResults.Add(new SubsessionResult
        {
            SubsessionId = 7,
            CustId = 100001,
            CarId = 1,
            BestLapSeconds = 91,
        });
        await demo.SaveChangesAsync(Ct);
        await using var real = shared.NewContext(DataProvenance.Real);
        Assert.Empty(await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            real.SubsessionResults, Ct));
    }

    [Fact]
    public async Task SameCustomerIdHasIndependentDemoAndRealWarmCaches()
    {
        await using var shared = DbContextFactory.CreateShared();
        await using var db = shared.NewContext();
        await using var demoDb = shared.NewContext(DataProvenance.Demo);
        var spec = IRacingCacheKeys.Profile(100001);
        var real = new CachedIRacingClient(db, Substitute.For<IDataClient>(), DataProvenance.Real);
        var demo = new CachedIRacingClient(demoDb, Substitute.For<IDataClient>(), DataProvenance.Demo);

        db.ExternalDataCaches.Add(new ExternalDataCache
        {
            CacheKey = spec.Key,
            Payload = "{\"Lap\":87}",
            FetchedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        });
        await db.SaveChangesAsync(Ct);
        await DemoCache.UpsertAsync(demoDb, spec.Key, new Evidence(91), Ct);
        Assert.Equal(new Evidence(91), await demo.GetOrFetchAsync<Evidence>(spec,
            _ => throw new InvalidOperationException("Demo must not fetch real evidence"), Ct));
        Assert.Equal(new Evidence(87), await real.GetOrFetchAsync<Evidence>(spec,
            _ => throw new InvalidOperationException("Real should have its own warm cache"), Ct));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrExpiredDemoCannotUseConfiguredRealProvider(bool expired)
    {
        await using var db = DbContextFactory.Create();
        var demo = new CachedIRacingClient(db, Substitute.For<IDataClient>(), DataProvenance.Demo);
        var realProviderReached = false;
        if (expired)
        {
            db.ExternalDataCaches.Add(new ExternalDataCache
            {
                Provenance = DataProvenance.Demo,
                CacheKey = IRacingCacheKeys.Profile(100001).Key,
                Payload = "{\"Lap\":91}",
                FetchedAt = DateTimeOffset.UtcNow.AddDays(-2),
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
            });
            await db.SaveChangesAsync(Ct);
        }

        await Assert.ThrowsAsync<IRacingNotConfiguredException>(() => demo.GetOrFetchAsync(
            IRacingCacheKeys.Profile(100001),
            _ => { realProviderReached = true; return Task.FromResult(new Evidence(91)); }, Ct));

        Assert.False(realProviderReached);
    }
}
