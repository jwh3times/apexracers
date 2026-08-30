using System.Text.Json;
using System.Data;
using ApexRacers.Api.Dtos;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Data.Migrations;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace ApexRacers.Tests.Data;

[Collection(PostgreSqlCollection.Name)]
public class CachedDriverIdentityMigrationTests(PostgreSqlFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string LapsKey = "laps:7:260514";
    private const string LapsPayload =
        """
        {"SubsessionId":7,"CustId":260514,"MeanSeconds":60.5,"StdDevSeconds":0.5,"FastestLapSeconds":60.0,"DegSlopeSecondsPerLap":0.1,"Laps":[{"LapNumber":1,"LapTimeSeconds":60.0,"Incident":true,"Valid":false},{"LapNumber":2,"LapTimeSeconds":-1.0,"Incident":false,"Valid":false},{"LapNumber":3,"LapTimeSeconds":0.0,"Incident":false,"Valid":false},{"LapNumber":4,"LapTimeSeconds":61.0,"Incident":false,"Valid":true}]}
        """;

    private const string LeaderboardKey = "leaderboard:5";
    private const string LeaderboardPayload =
        """[{"CategoryId":5,"Rank":1,"CustId":260514,"Driver":"Leaderboard Driver","Location":"US","Starts":1,"Wins":1,"IRating":5000,"TtRating":2000,"ChampPoints":100}]""";

    private const string StandingsKey = "standings:1:2";
    private const string StandingsPayload =
        """[{"Rank":1,"CustId":260514,"DriverName":"Standing Driver","Division":1,"Starts":1,"Wins":1,"Top5":1,"Poles":1,"Points":100,"AvgFinishPosition":1.0,"Incidents":0}]""";

    private const string TtStandingsKey = "tt-standings:1:2";
    private const string TtStandingsPayload =
        """[{"Rank":1,"CustId":260514,"DriverName":"TT Driver","Division":1,"TtRating":2000,"Starts":1,"Wins":1,"Top5":1,"Poles":1,"Points":100,"AvgFinishPosition":1.0,"Incidents":0}]""";

    private const string QualifyKey = "qual:1:2:3";
    private const string QualifyPayload =
        """[{"Rank":1,"CustId":260514,"DriverName":"Qualifying Driver","Division":1,"IRating":5000,"BestQualLapSeconds":60.0,"Week":3}]""";

    private const string DriverSearchKey = "driversearch:jerry";
    private const string DriverSearchPayload =
        """[{"CustId":260514,"DisplayName":"Search Driver"}]""";

    private const string UnrelatedKey = "profile:260514";
    private const string UnrelatedPayload =
        """{"CustId":999,"DisplayName":"Upstream SDK shape","Valid":false}""";

    [Fact]
    public async Task UpAndDown_RewriteOnlyAuditedCacheContracts()
    {
        await using var db = await postgres.CreateDbContextAsync(Ct);
        var now = DateTimeOffset.UtcNow;
        db.ExternalDataCaches.AddRange(
            Row(LapsKey, LapsPayload, now),
            Row(LeaderboardKey, LeaderboardPayload, now),
            Row(StandingsKey, StandingsPayload, now),
            Row(TtStandingsKey, TtStandingsPayload, now),
            Row(QualifyKey, QualifyPayload, now),
            Row(DriverSearchKey, DriverSearchPayload, now),
            Row(UnrelatedKey, UnrelatedPayload, now));
        await db.SaveChangesAsync(Ct);

        var migration = new UnifyCachedDriverIdentityNames();
        var up = Assert.IsType<SqlOperation>(Assert.Single(migration.UpOperations));
        await ExecuteSqlAsync(db, up.Sql);
        db.ChangeTracker.Clear();

        var laps = JsonSerializer.Deserialize<DriverLapsDto>(await PayloadAsync(db, LapsKey));
        Assert.NotNull(laps);
        Assert.Equal(260514, laps.CustomerId);
        Assert.Equal([true, false, false, true], laps.Laps.Select(lap => lap.Timed));

        AssertIdentity(await PayloadAsync(db, LeaderboardKey), "Leaderboard Driver");
        AssertIdentity(await PayloadAsync(db, StandingsKey), "Standing Driver");
        AssertIdentity(await PayloadAsync(db, TtStandingsKey), "TT Driver");
        AssertIdentity(await PayloadAsync(db, QualifyKey), "Qualifying Driver");
        AssertIdentity(await PayloadAsync(db, DriverSearchKey), "Search Driver");
        Assert.Equal(UnrelatedPayload, await PayloadAsync(db, UnrelatedKey));

        var down = Assert.IsType<SqlOperation>(Assert.Single(migration.DownOperations));
        await ExecuteSqlAsync(db, down.Sql);
        db.ChangeTracker.Clear();

        AssertLegacyIdentity(await PayloadAsync(db, LeaderboardKey), "Driver", "Leaderboard Driver");
        AssertLegacyIdentity(await PayloadAsync(db, StandingsKey), "DriverName", "Standing Driver");
        AssertLegacyIdentity(await PayloadAsync(db, TtStandingsKey), "DriverName", "TT Driver");
        AssertLegacyIdentity(await PayloadAsync(db, QualifyKey), "DriverName", "Qualifying Driver");
        AssertLegacyIdentity(await PayloadAsync(db, DriverSearchKey), "DisplayName", "Search Driver");

        using var legacyLaps = JsonDocument.Parse(await PayloadAsync(db, LapsKey));
        Assert.Equal(260514, legacyLaps.RootElement.GetProperty("CustId").GetInt64());
        Assert.False(legacyLaps.RootElement.TryGetProperty("CustomerId", out _));
        Assert.Equal(
            [true, false, false, true],
            legacyLaps.RootElement.GetProperty("Laps").EnumerateArray()
                .Select(lap => lap.GetProperty("Valid").GetBoolean()));
        Assert.Equal(UnrelatedPayload, await PayloadAsync(db, UnrelatedKey));
    }

    private static ExternalDataCache Row(string key, string payload, DateTimeOffset now) => new()
    {
        CacheKey = key,
        Payload = payload,
        FetchedAt = now,
        ExpiresAt = now.AddDays(1),
    };

    private static async Task<string> PayloadAsync(AppDbContext db, string key) =>
        await db.ExternalDataCaches.AsNoTracking()
            .Where(row => row.CacheKey == key)
            .Select(row => row.Payload)
            .SingleAsync(Ct);

    private static async Task ExecuteSqlAsync(AppDbContext db, string sql)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(Ct);
    }

    private static void AssertIdentity(string payload, string driverName)
    {
        using var json = JsonDocument.Parse(payload);
        var row = json.RootElement[0];
        Assert.Equal(260514, row.GetProperty("CustomerId").GetInt64());
        Assert.Equal(driverName, row.GetProperty("DriverName").GetString());
        Assert.False(row.TryGetProperty("CustId", out _));
        Assert.False(row.TryGetProperty("DisplayName", out _));
        Assert.False(row.TryGetProperty("Driver", out _));
    }

    private static void AssertLegacyIdentity(string payload, string nameProperty, string driverName)
    {
        using var json = JsonDocument.Parse(payload);
        var row = json.RootElement[0];
        Assert.Equal(260514, row.GetProperty("CustId").GetInt64());
        Assert.Equal(driverName, row.GetProperty(nameProperty).GetString());
        Assert.False(row.TryGetProperty("CustomerId", out _));
    }
}
