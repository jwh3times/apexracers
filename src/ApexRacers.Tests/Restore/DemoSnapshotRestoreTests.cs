using System.Net;
using System.Net.Http.Headers;
using ApexRacers.Api.Services;
using ApexRacers.Core;
using ApexRacers.Data;
using ApexRacers.Seeder;
using ApexRacers.Seeder.Demo;
using ApexRacers.Seeder.Verification;
using ApexRacers.Tests.Helpers;
using ApexRacers.Tests.References;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace ApexRacers.Tests.Restore;

[Collection(PostgreSqlCollection.Name)]
public sealed class DemoSnapshotRestoreTests(PostgreSqlFixture postgres)
{
    [Fact]
    public async Task Restored_Demo_is_useful_but_actual_teardown_precedes_closed_Real_simulation()
    {
        var ct = TestContext.Current.CancellationToken;
        var name = "apexracers_restore_legacy_" + Guid.NewGuid().ToString("N");
        await using (var connection = new NpgsqlConnection(postgres.Container.GetConnectionString()))
        {
            await connection.OpenAsync(ct);
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await create.ExecuteNonQueryAsync(ct);
        }
        var target = new NpgsqlConnectionStringBuilder(postgres.Container.GetConnectionString()) { Database = name, Host = "127.0.0.1", Pooling = false }.ConnectionString;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(target, o => o.MigrationsHistoryTable("__EFMigrationsHistory", "iracing")).Options;
        await using var demo = new AppDbContext(options, new IRacingDataScope(DataProvenance.Demo));
        await demo.Database.MigrateAsync(ct);
        await new CiCatalogSeeder(demo).SeedAsync();
        await new DemoCacheSeeder(demo).SeedAllAsync(ct);
        demo.Users.Add(new() { Id = ReferenceActors.Recipient, DisplayName = "Synthetic restored Demo User", EmailConfirmed = true });
        var flag = await demo.FeatureFlags.SingleAsync(f => f.Key == "iracing-demo", ct);
        flag.IsEnabled = true; flag.MinimumRole = "Standard";
        await demo.SaveChangesAsync(ct);
        var snapshot = await PrimarySnapshot.CaptureAsync(postgres, target, ct);
        var passed = false;
        var providerReached = false;
        try
        {
            // The restored backup really contains enabled Demo data; identifiers never classify it Real.
            await snapshot.RestoreAsync(postgres, target, ct);
            demo.ChangeTracker.Clear();
            Assert.All(await DemoSeedVerifier.VerifyDemoAsync(demo, ct), check => Assert.True(check.Passed, check.Detail));
            await using (var ordinary = await LegacyApiProcess.StartAsync(target, ct, current: true))
            {
                using var request = Request("/api/users/me/profile-stats");
                using var useful = await ordinary.Client.SendAsync(request, ct);
                Assert.Equal(HttpStatusCode.OK, useful.StatusCode);
                Assert.True(useful.Headers.CacheControl?.NoStore);
                Assert.Contains("Demo", await useful.Content.ReadAsStringAsync(ct));
            }
            flag = await demo.FeatureFlags.SingleAsync(f => f.Key == "iracing-demo", ct);
            flag.IsEnabled = false; await demo.SaveChangesAsync(ct);
            var sql = await File.ReadAllTextAsync(Path.Combine(LegacyApiProcess.RepositoryRoot, "src/ApexRacers.Data/Seeds/purge_demo_data.sql"), ct);
            await demo.Database.ExecuteSqlRawAsync(sql, ct);
            await using var real = new AppDbContext(options, new IRacingDataScope(DataProvenance.Real));
            Assert.All(await DemoSeedVerifier.VerifyTeardownAsync(real, ct), check => Assert.True(check.Passed, check.Detail));
            Assert.Empty(await real.ExternalDataCaches.IgnoreQueryFilters().Where(c => c.Provenance == DataProvenance.Demo).ToListAsync(ct));
            Assert.Empty(await real.SubsessionResults.IgnoreQueryFilters().Where(c => c.Provenance == DataProvenance.Demo).ToListAsync(ct));
            await Assert.ThrowsAsync<IRacingNotConfiguredException>(() => new CachedIRacingClient(real, null).GetOrFetchAsync<int>(IRacingCacheKeys.Profile(DemoData.DriverCustId), _ =>
            { providerReached = true; return Task.FromResult(17); }, ct));
            Assert.False(providerReached);
            await using (var ordinary = await LegacyApiProcess.StartAsync(target, ct, current: true))
            {
                using var request = Request("/api/users/me/profile-stats");
                using var closed = await ordinary.Client.SendAsync(request, ct);
                Assert.Equal(HttpStatusCode.ServiceUnavailable, closed.StatusCode);
                Assert.True(closed.Headers.CacheControl?.NoStore);
            }
            passed = true;
        }
        finally
        {
            await snapshot.RecordAsync("demo-teardown-before-real", new
            {
                UsefulRestoredDemo = true,
                Teardown = "actual purge SQL and verification",
                RealOutput = 503,
                ProviderReached = providerReached,
                Topology = "actual migration-chain primary, ordinary API startup, fabricated --ci catalog/cache; no provider credentials"
            }, passed);
        }
    }

    private static HttpRequestMessage Request(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ReferenceActors.Token(ReferenceActors.Recipient));
        return request;
    }
}
