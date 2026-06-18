using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Aydsko.iRacingData;
using Aydsko.iRacingData.Common;
using Aydsko.iRacingData.Stats;
using NSubstitute;
using Xunit;

namespace ApexRacers.Tests.Services;

public class RaceHistoryServiceTests
{
    private const long CustId = 691062;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class StubServiceProvider(object? service) : IServiceProvider
    {
        public object? GetService(Type serviceType) => service;
    }

    private static Race Race(
        int subsessionId, string when, string seriesName, int trackId, string trackName,
        int carId, int start, int finish, int incidents,
        int oldIr, int newIr, int oldSub, int newSub, int sof, int points) => new()
    {
        SubsessionId = subsessionId,
        SessionStartTime = DateTimeOffset.Parse(when),
        SeriesName = seriesName,
        Track = new Aydsko.iRacingData.Stats.Track { TrackId = trackId, TrackName = trackName },
        CarId = carId,
        StartPosition = start,
        FinishPosition = finish,
        Incidents = incidents,
        OldiRating = oldIr,
        NewiRating = newIr,
        OldSubLevel = oldSub,
        NewSubLevel = newSub,
        StrengthOfField = sof,
        Points = points,
    };

    private static (RaceHistoryService Service, IDataClient Client, AppDbContext Db) Build(
        params Race[] races)
    {
        var client = Substitute.For<IDataClient>();
        client.GetMemberRecentRacesAsync(Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new DataResponse<MemberRecentRaces>
            {
                Data = new MemberRecentRaces { CustomerId = (int)CustId, Races = races },
            });

        var db = DbContextFactory.Create();
        var cached = new CachedIRacingClient(db, new StubServiceProvider(client));
        return (new RaceHistoryService(cached, db), client, db);
    }

    [Fact]
    public async Task GetRecentRacesAsync_MapsDeltasNamesAndOrdersNewestFirst()
    {
        var (service, _, db) = Build(
            Race(100, "2026-05-01T10:00:00Z", "GT3 Cup", 532, "Thruxton", 132,
                start: 11, finish: 3, incidents: 5, oldIr: 1907, newIr: 1854,
                oldSub: 237, newSub: 238, sof: 2502, points: 13),
            Race(200, "2026-05-29T17:15:00Z", "GT4 Cup", 47, "Laguna Seca", 119,
                start: 20, finish: 11, incidents: 7, oldIr: 1806, newIr: 1834,
                oldSub: 240, newSub: 226, sof: 1833, points: 73));
        db.Cars.Add(new Car { Id = 132, Name = "BMW M4 GT3 EVO", NameAbbreviated = "BMW" });
        db.Cars.Add(new Car { Id = 119, Name = "Porsche 718 GT4", NameAbbreviated = "POR" });
        await db.SaveChangesAsync(Ct);

        var rows = await service.GetRecentRacesAsync(CustId, Ct);

        Assert.Equal(2, rows.Count);
        // Newest first → subsession 200 (May 29) leads.
        Assert.Equal(200, rows[0].SubsessionId);
        Assert.Equal(100, rows[1].SubsessionId);

        var win = rows[1]; // subsession 100
        Assert.Equal("GT3 Cup", win.SeriesName);
        Assert.Equal("Thruxton", win.TrackName);
        Assert.Equal("BMW M4 GT3 EVO", win.CarName);
        Assert.Equal(3, win.FinishPosition);
        Assert.Equal(1854 - 1907, win.IRatingDelta); // -53
        Assert.Equal((238 - 237) / 100.0, win.SrDelta, precision: 4); // +0.01
        Assert.Equal(2502, win.StrengthOfField);
        Assert.Equal(13, win.Points);

        var lossSr = rows[0]; // subsession 200
        Assert.Equal(1834 - 1806, lossSr.IRatingDelta); // +28
        Assert.Equal((226 - 240) / 100.0, lossSr.SrDelta, precision: 4); // -0.14
    }

    [Fact]
    public async Task GetRecentRacesAsync_UnknownCar_FallsBackToCarIdLabel()
    {
        var (service, _, db) = Build(
            Race(300, "2026-05-01T10:00:00Z", "Series", 1, "Track", 999,
                1, 1, 0, 1000, 1000, 200, 200, 1500, 10));
        // No Car row for 999.
        await db.SaveChangesAsync(Ct);

        var row = Assert.Single(await service.GetRecentRacesAsync(CustId, Ct));
        Assert.Equal("Car 999", row.CarName);
    }

    [Fact]
    public async Task GetRecentRacesAsync_NoRaces_ReturnsEmpty()
    {
        var (service, _, db) = Build();
        await using var _db = db;

        Assert.Empty(await service.GetRecentRacesAsync(CustId, Ct));
    }

    [Fact]
    public async Task GetRecentRacesAsync_SecondCall_ServedFromCache_FetchesOnce()
    {
        var (service, client, db) = Build(
            Race(100, "2026-05-01T10:00:00Z", "GT3 Cup", 532, "Thruxton", 132,
                11, 3, 5, 1907, 1854, 237, 238, 2502, 13));
        await using var _db = db;

        var first = await service.GetRecentRacesAsync(CustId, Ct);
        var second = await service.GetRecentRacesAsync(CustId, Ct);

        Assert.Equal(first.Count, second.Count);
        Assert.Equal(first[0].SubsessionId, second[0].SubsessionId);
        await client.Received(1).GetMemberRecentRacesAsync(Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }
}
