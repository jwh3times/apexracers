using System.Runtime.CompilerServices;
using ApexRacers.Api.Services;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Seeder;
using ApexRacers.Seeder.Demo;
using ApexRacers.Seeder.Verification;
using ApexRacers.Tests.Helpers;
using Aydsko.iRacingData;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Npgsql;
using Xunit;

namespace ApexRacers.Tests.Data;

[Collection(PostgreSqlCollection.Name)]
public class DemoTransitionTests(PostgreSqlFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task IdenticalRaceAndDriverIdsRemainDistinctAndCrossNamespaceParentIsRejected()
    {
        var options = await postgres.CreateOptionsAsync(Ct);
        await using var demo = new AppDbContext(options, new IRacingDataScope(DataProvenance.Demo));
        await new CiCatalogSeeder(demo).SeedAsync();
        var synthetic = await demo.Subsessions.FirstAsync(Ct);
        var syntheticDriver = await demo.SubsessionResults.FirstAsync(r => r.SubsessionId == synthetic.Id, Ct);
        await using var real = new AppDbContext(options, new IRacingDataScope(DataProvenance.Real));
        Assert.Empty(await real.Subsessions.ToListAsync(Ct));
        real.SubsessionResults.Add(new SubsessionResult
        {
            SubsessionId = synthetic.Id, CustId = syntheticDriver.CustId,
            CarId = syntheticDriver.CarId, CarClassId = syntheticDriver.CarClassId, BestLapSeconds = 87,
        });
        var rejected = await Assert.ThrowsAsync<DbUpdateException>(() => real.SaveChangesAsync(Ct));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(rejected.InnerException).SqlState);
        Assert.Equal("FK_RaceEvidenceResults_RaceEvidenceSubsessions_Provenance_Subs~",
            Assert.IsType<PostgresException>(rejected.InnerException).ConstraintName);
        real.ChangeTracker.Clear();
        real.Subsessions.Add(new Subsession
        {
            Id = synthetic.Id, SeasonId = synthetic.SeasonId, TrackId = synthetic.TrackId,
            StartTime = synthetic.StartTime,
        });
        real.SubsessionResults.Add(new SubsessionResult
        {
            SubsessionId = synthetic.Id, CustId = syntheticDriver.CustId,
            CarId = syntheticDriver.CarId, CarClassId = syntheticDriver.CarClassId, BestLapSeconds = 87,
        });
        await real.SaveChangesAsync(Ct);
        Assert.Equal(87, (await real.SubsessionResults.SingleAsync(Ct)).BestLapSeconds);
        Assert.Equal(syntheticDriver.BestLapSeconds, (await demo.SubsessionResults.AsNoTracking()
            .SingleAsync(r => r.SubsessionId == synthetic.Id && r.CustId == syntheticDriver.CustId, Ct)).BestLapSeconds);
        Assert.Equal(2, await real.Subsessions.IgnoreQueryFilters().CountAsync(s => s.Id == synthetic.Id, Ct));
    }

    [Fact]
    public async Task IdempotentDemoSeedAndSqlTeardownPrecedeRealAcquisition()
    {
        var options = await postgres.CreateOptionsAsync(Ct);
        await using var real = new AppDbContext(options, new IRacingDataScope(DataProvenance.Real));
        await using var demo = new AppDbContext(options, new IRacingDataScope(DataProvenance.Demo));
        demo.FeatureFlags.Add(new FeatureFlag
        {
            Key = "iracing-demo", Name = "Demo", IsEnabled = true, MinimumRole = "Standard",
        });
        await demo.SaveChangesAsync(Ct);
        var spec = IRacingCacheKeys.Profile(DemoData.DriverCustId);
        var future = new DateTimeOffset(9999, 1, 1, 0, 0, 0, TimeSpan.Zero);
        real.ExternalDataCaches.Add(new ExternalDataCache
        {
            CacheKey = spec.Key, Payload = "17", ExpiresAt = future, FetchedAt = DateTimeOffset.UtcNow,
        });
        real.QuarantinedDataCaches.Add(new QuarantinedDataCache
        {
            CacheKey = "profile:legacy", Payload = "{\"unclassified\":true}", ExpiresAt = future,
            FetchedAt = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
        });
        await real.SaveChangesAsync(Ct);
        await new CiCatalogSeeder(demo).SeedAsync();
        await new DemoCacheSeeder(demo).SeedAllAsync(Ct);
        var cacheCount = await demo.ExternalDataCaches.CountAsync(c => c.Provenance == DataProvenance.Demo, Ct);
        var raceCount = await demo.Subsessions.CountAsync(Ct);
        await new CiCatalogSeeder(demo).SeedAsync();
        await new DemoCacheSeeder(demo).SeedAllAsync(Ct);
        Assert.Equal(cacheCount, await demo.ExternalDataCaches.CountAsync(c => c.Provenance == DataProvenance.Demo, Ct));
        Assert.Equal(raceCount, await demo.Subsessions.CountAsync(Ct));
        Assert.All(await DemoSeedVerifier.VerifyDemoAsync(demo, Ct), check => Assert.True(check.Passed, check.Detail));

        var providerReached = false;
        var client = new CachedIRacingClient(real, Substitute.For<IDataClient>());
        var cold = IRacingCacheKeys.Profile(777);
        await Assert.ThrowsAsync<DemoTeardownRequiredException>(() => client.GetOrFetchAsync(cold, _ =>
        {
            providerReached = true;
            return Task.FromResult(23);
        }, Ct));
        Assert.False(providerReached);
        var flag = await demo.FeatureFlags.SingleAsync(f => f.Key == "iracing-demo", Ct);
        flag.IsEnabled = false;
        // Expiry has no bearing on origin: this ordinary expired Demo row must also be purged.
        var ordinaryDemo = await demo.ExternalDataCaches.SingleAsync(c => c.Provenance == DataProvenance.Demo && c.CacheKey == spec.Key, Ct);
        ordinaryDemo.ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1);
        await demo.SaveChangesAsync(Ct);
        await real.Database.ExecuteSqlRawAsync(await File.ReadAllTextAsync(PurgePath(), Ct), Ct);
        Assert.All(await DemoSeedVerifier.VerifyTeardownAsync(real, Ct), check => Assert.True(check.Passed, check.Detail));
        Assert.Equal(1, await real.ExternalDataCaches.CountAsync(Ct));
        Assert.Equal(1, await real.QuarantinedDataCaches.CountAsync(Ct));
        Assert.Equal(future, (await real.ExternalDataCaches.AsNoTracking().SingleAsync(Ct)).ExpiresAt);
        Assert.Equal(23, await client.GetOrFetchAsync(cold, _ =>
        {
            providerReached = true;
            return Task.FromResult(23);
        }, Ct));
        Assert.True(providerReached);
        Assert.Equal(DataProvenance.Real, (await real.ExternalDataCaches.SingleAsync(c => c.CacheKey == cold.Key, Ct)).Provenance);
    }

    [Fact]
    public async Task RealFetchCompletingAfterDemoStartsCannotPublishOrRenewItsStaleCache()
    {
        var options = await postgres.CreateOptionsAsync(Ct);
        await using var real = new AppDbContext(options, new IRacingDataScope(DataProvenance.Real));
        await using var demo = new AppDbContext(options, new IRacingDataScope(DataProvenance.Demo));
        var spec = IRacingCacheKeys.Profile(DemoData.DriverCustId);
        var fetched = DateTimeOffset.UtcNow.AddDays(-2);
        real.ExternalDataCaches.Add(new ExternalDataCache
        {
            CacheKey = spec.Key, Payload = "17", FetchedAt = fetched, ExpiresAt = fetched.AddHours(1),
        });
        await real.SaveChangesAsync(Ct);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var complete = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fetch = new CachedIRacingClient(real, Substitute.For<IDataClient>()).GetOrFetchAsync(spec, _ =>
        {
            entered.SetResult();
            return complete.Task;
        }, Ct);
        await entered.Task.WaitAsync(Ct);
        await DemoCache.UpsertAsync(demo, spec.Key, 91, Ct);
        complete.SetResult(23);
        await Assert.ThrowsAsync<DemoTeardownRequiredException>(() => fetch);
        var realRow = await real.ExternalDataCaches.AsNoTracking().SingleAsync(c => c.Provenance == DataProvenance.Real, Ct);
        Assert.Equal("17", realRow.Payload);
        Assert.Equal(fetched.ToUnixTimeMilliseconds(), realRow.FetchedAt.ToUnixTimeMilliseconds());
        Assert.Equal(91, await new CachedIRacingClient(demo, Substitute.For<IDataClient>()).GetOrFetchAsync<int>(spec,
            _ => throw new InvalidOperationException("Synthetic evidence must remain offline"), Ct));
    }

    private static string PurgePath([CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "../../ApexRacers.Data/Seeds/purge_demo_data.sql"));
}


