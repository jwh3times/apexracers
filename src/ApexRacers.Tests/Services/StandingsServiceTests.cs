using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Aydsko.iRacingData;
using NSubstitute;
using Xunit;

namespace ApexRacers.Tests.Services;

public class StandingsServiceTests
{
    private const int SeriesId = 444;
    private const int SeasonId = 6115;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class StubServiceProvider(object? service) : IServiceProvider
    {
        public object? GetService(Type serviceType) => service;
    }

    private static Aydsko.iRacingData.Stats.SeasonDriverStanding Standing(
        int rank, int custId, string name, int points, int wins) => new()
    {
        Rank = rank,
        CustomerId = custId,
        DisplayName = name,
        Division = 1,
        Starts = 12,
        Wins = wins,
        Top5 = 6,
        Poles = 2,
        Points = points,
        AverageFinishPosition = 5.4m,
        Incidents = 30,
    };

    private static (StandingsService Service, IDataClient Client, AppDbContext Db) Build(
        bool activeSeason = true, bool withClasses = true,
        params Aydsko.iRacingData.Stats.SeasonDriverStanding[] standings)
    {
        var db = DbContextFactory.Create();
        db.Series.Add(new Series { Id = SeriesId, Name = "GT3 Cup" });
        db.Seasons.Add(new Season { Id = SeasonId, SeriesId = SeriesId, Active = activeSeason, Year = 2026, Quarter = 2 });
        if (withClasses)
        {
            db.CarClasses.Add(new CarClass { Id = 4091, Name = "GT3 Class", ShortName = "GT3", RelativeSpeed = 50 });
            db.CarClasses.Add(new CarClass { Id = 2000, Name = "GT4 Class", ShortName = "GT4", RelativeSpeed = 40 });
            db.SeasonCarClasses.Add(new SeasonCarClass { SeasonId = SeasonId, CarClassId = 4091 });
            db.SeasonCarClasses.Add(new SeasonCarClass { SeasonId = SeasonId, CarClassId = 2000 });
        }
        db.SaveChanges();

        var client = Substitute.For<IDataClient>();
        client.GetSeasonDriverStandingsAsync(
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new Aydsko.iRacingData.Common.DataResponse<(
                Aydsko.iRacingData.Stats.SeasonDriverStandingsHeader, Aydsko.iRacingData.Stats.SeasonDriverStanding[])>
            {
                Data = (new Aydsko.iRacingData.Stats.SeasonDriverStandingsHeader(), standings),
            });

        var cached = new CachedIRacingClient(db, new StubServiceProvider(client));
        return (new StandingsService(db, cached), client, db);
    }

    [Fact]
    public async Task GetDriverStandingsAsync_DefaultsToFirstClass_MapsAndOrders()
    {
        var (service, _, db) = Build(
            standings:
            [
                Standing(2, 222, "Second", 880, 3),
                Standing(1, 111, "Leader", 950, 7),
            ]);
        await using var _db = db;

        var dto = await service.GetDriverStandingsAsync(SeriesId, carClassId: null, Ct);

        Assert.Equal("GT3 Cup", dto.SeriesName);
        Assert.Equal(4091, dto.CarClassId); // "GT3 Class" sorts before "GT4 Class"
        Assert.Equal("GT3 Class", dto.CarClassName);
        Assert.Equal(2, dto.CarClasses.Count);

        // Ordered by rank.
        Assert.Equal(new[] { 1, 2 }, dto.Standings.Select(s => s.Rank));
        Assert.Equal("Leader", dto.Standings[0].DriverName);
        Assert.Equal(950, dto.Standings[0].Points);
        Assert.Equal(7, dto.Standings[0].Wins);
        Assert.Equal(5.4, dto.Standings[0].AvgFinishPosition, precision: 3);
    }

    [Fact]
    public async Task GetDriverStandingsAsync_RespectsRequestedClass()
    {
        var (service, _, db) = Build(standings: [Standing(1, 111, "Leader", 950, 7)]);
        await using var _db = db;

        var dto = await service.GetDriverStandingsAsync(SeriesId, carClassId: 2000, Ct);

        Assert.Equal(2000, dto.CarClassId);
        Assert.Equal("GT4 Class", dto.CarClassName);
    }

    [Fact]
    public async Task GetDriverStandingsAsync_SecondCall_ServedFromCache_FetchesOnce()
    {
        var (service, client, db) = Build(standings: [Standing(1, 111, "Leader", 950, 7)]);
        await using var _db = db;

        await service.GetDriverStandingsAsync(SeriesId, null, Ct);
        var second = await service.GetDriverStandingsAsync(SeriesId, null, Ct);

        Assert.Single(second.Standings);
        await client.Received(1).GetSeasonDriverStandingsAsync(
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetDriverStandingsAsync_NoActiveSeason_ThrowsKeyNotFound()
    {
        var (service, _, db) = Build(activeSeason: false, standings: [Standing(1, 111, "Leader", 950, 7)]);
        await using var _db = db;

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.GetDriverStandingsAsync(SeriesId, null, Ct));
    }

    [Fact]
    public async Task GetDriverStandingsAsync_NoCarClasses_ThrowsKeyNotFound()
    {
        var (service, _, db) = Build(withClasses: false, standings: [Standing(1, 111, "Leader", 950, 7)]);
        await using var _db = db;

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.GetDriverStandingsAsync(SeriesId, null, Ct));
    }
}
