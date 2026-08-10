using System.Text.Json;
using System.Globalization;
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
        await using var db = DbContextFactory.Create();

        await DemoCache.UpsertAsync(db, "sample:1", new Sample(7, "x"), Ct);

        var row = await db.ExternalDataCaches.SingleAsync(Ct);
        Assert.Equal("sample:1", row.CacheKey);
        Assert.Equal(DemoCache.Sentinel, row.ExpiresAt);
        Assert.Equal(new Sample(7, "x"), JsonSerializer.Deserialize<Sample>(row.Payload));
    }

    [Fact]
    public async Task UpsertAsync_SameKeyTwice_UpdatesInPlace_NoDuplicate()
    {
        await using var db = DbContextFactory.Create();

        await DemoCache.UpsertAsync(db, "sample:1", new Sample(1, "a"), Ct);
        await DemoCache.UpsertAsync(db, "sample:1", new Sample(2, "b"), Ct);

        var row = await db.ExternalDataCaches.SingleAsync(Ct);
        Assert.Equal(new Sample(2, "b"), JsonSerializer.Deserialize<Sample>(row.Payload));
    }

    [Fact]
    public void Sentinel_IsInsideTheOwnedSentinelRange() =>
        Assert.True(DemoCache.Sentinel >= DemoCache.SentinelThreshold);

    [Fact]
    public void ProductionPurgeSql_MatchesTheOwnedThresholdAndRangeOperator()
    {
        var path = PurgeSqlPath();
        var sql = File.ReadAllText(path);
        var delete = Regex.Match(
            sql,
            """DELETE\s+FROM\s+iracing\."ExternalDataCaches"\s+WHERE\s+"ExpiresAt"\s*(?<operator>>=|<=|=|>|<)\s*TIMESTAMPTZ\s*'(?<threshold>[^']+)'\s*;""",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        Assert.True(delete.Success,
            $"Could not find the production ExternalDataCaches sentinel DELETE in {path}.");
        Assert.Equal(">=", delete.Groups["operator"].Value);
        var threshold = DateTimeOffset.Parse(
            delete.Groups["threshold"].Value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal);
        Assert.Equal(
            DemoCache.SentinelThreshold,
            threshold);
        Assert.Contains("DemoCache.SentinelThreshold", sql, StringComparison.Ordinal);
        Assert.Contains("DemoData.CacheSentinelThreshold", sql, StringComparison.Ordinal);
    }

    private static string PurgeSqlPath([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(sourceFile)!, "..", "..", "ApexRacers.Data", "Seeds",
            "purge_demo_data.sql"));
}
