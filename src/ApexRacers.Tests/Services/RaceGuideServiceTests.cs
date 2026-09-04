using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Aydsko.iRacingData;
using NSubstitute;
using Xunit;

namespace ApexRacers.Tests.Services;

public class RaceGuideServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;


    private static Aydsko.iRacingData.Series.RaceGuideSession Session(
        int seriesId, double startOffsetMin, double endOffsetMin, int entries = 40) => new()
    {
        SeriesId = seriesId,
        StartTime = DateTime.UtcNow.AddMinutes(startOffsetMin),
        EndTime = DateTime.UtcNow.AddMinutes(endOffsetMin),
        EntryCount = entries,
        RaceWeekNumber = 5,
    };

    private static (RaceGuideService Service, IDataClient Client, AppDbContext Db) Build(
        params Aydsko.iRacingData.Series.RaceGuideSession[] sessions)
    {
        var db = DbContextFactory.Create();
        db.Series.Add(new Series { Id = 231, Name = "GT Sprint" });
        db.SaveChanges();

        var client = Substitute.For<IDataClient>();
        client.GetRaceGuideAsync(Arg.Any<DateTimeOffset?>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>())
            .Returns(new Aydsko.iRacingData.Common.DataResponse<Aydsko.iRacingData.Series.RaceGuideResults>
            {
                Data = new Aydsko.iRacingData.Series.RaceGuideResults { Sessions = sessions },
            });

        var cached = new CachedIRacingClient(db, client);
        return (new RaceGuideService(cached, db), client, db);
    }

    [Fact]
    public async Task GetGuideAsync_KeepsUpcomingAndInProgress_OrdersByStart_JoinsNames()
    {
        var (service, _, db) = Build(
            Session(231, startOffsetMin: 30, endOffsetMin: 60), // upcoming (named)
            Session(999, startOffsetMin: -10, endOffsetMin: 20), // in progress (unnamed → fallback)
            Session(100, startOffsetMin: 300, endOffsetMin: 330), // beyond 3h → excluded
            Session(101, startOffsetMin: -120, endOffsetMin: -60)); // already ended → excluded
        await using var _db = db;

        var rows = await service.GetGuideAsync(Ct);

        Assert.Equal(2, rows.Count);
        // Ordered by start: the in-progress session (−10m) precedes the +30m one.
        Assert.Equal(999, rows[0].SeriesId);
        Assert.Equal("Series 999", rows[0].SeriesName); // fallback when not in catalog
        Assert.Equal(231, rows[1].SeriesId);
        Assert.Equal("GT Sprint", rows[1].SeriesName);
        Assert.Equal(40, rows[1].EntryCount);
        Assert.Equal(5, rows[1].RaceWeekIndex);
    }

    [Fact]
    public async Task GetGuideAsync_NoSessions_ReturnsEmpty()
    {
        var (service, _, db) = Build();
        await using var _db = db;

        Assert.Empty(await service.GetGuideAsync(Ct));
    }

    [Fact]
    public async Task GetGuideAsync_SecondCall_ServedFromCache_FetchesOnce()
    {
        var (service, client, db) = Build(Session(231, 30, 60));
        await using var _db = db;

        await service.GetGuideAsync(Ct);
        var second = await service.GetGuideAsync(Ct);

        Assert.Single(second);
        await client.Received(1).GetRaceGuideAsync(
            Arg.Any<DateTimeOffset?>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>());
    }
}
