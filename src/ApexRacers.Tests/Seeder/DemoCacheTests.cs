using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ApexRacers.Seeder.Demo;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ApexRacers.Tests.Seeder;

public class DemoCacheTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Sample(int A, string B);

    [Fact]
    public async Task UpsertAsync_InsertsRow_WithSentinelExpiry_AndRoundTrips()
    {
        await using var db = DbContextFactory.Create(ApexRacers.Core.DataProvenance.Demo);

        await DemoCache.UpsertAsync(db, "sample:1", new Sample(7, "x"), Ct);

        var row = await db.ExternalDataCaches.SingleAsync(Ct);
        Assert.Equal("sample:1", row.CacheKey);
        Assert.Equal(DemoCache.Sentinel, row.ExpiresAt);
        Assert.Equal(new Sample(7, "x"), JsonSerializer.Deserialize<Sample>(row.Payload));
    }

    [Fact]
    public async Task UpsertAsync_SameKeyTwice_UpdatesInPlace_NoDuplicate()
    {
        await using var db = DbContextFactory.Create(ApexRacers.Core.DataProvenance.Demo);

        await DemoCache.UpsertAsync(db, "sample:1", new Sample(1, "a"), Ct);
        await DemoCache.UpsertAsync(db, "sample:1", new Sample(2, "b"), Ct);

        var row = await db.ExternalDataCaches.SingleAsync(Ct);
        Assert.Equal(new Sample(2, "b"), JsonSerializer.Deserialize<Sample>(row.Payload));
    }

    [Fact]
    public void DemoFreshnessExpiryIsFarInTheFuture() =>
        Assert.True(DemoCache.Sentinel >= new DateTimeOffset(9000, 1, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void ProductionPurgeSql_UsesExplicitDemoNamespaceInsteadOfExpiry()
    {
        var path = PurgeSqlPath();
        var sql = File.ReadAllText(path);
        var delete = Regex.Match(
            sql,
            """DELETE\s+FROM\s+iracing\."MappedDataCaches"\s+WHERE\s+"Provenance"\s*=\s*(?<namespace>\d+)\s*;""",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        Assert.True(delete.Success,
            $"Could not find the mapped cache namespace DELETE in {path}.");
        Assert.Equal(((int)ApexRacers.Core.DataProvenance.Demo).ToString(), delete.Groups["namespace"].Value);
        Assert.DoesNotContain("\"ExpiresAt\"", sql, StringComparison.Ordinal);
    }

    private static string PurgeSqlPath([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(sourceFile)!, "..", "..", "ApexRacers.Data", "Seeds",
            "purge_demo_data.sql"));
}
