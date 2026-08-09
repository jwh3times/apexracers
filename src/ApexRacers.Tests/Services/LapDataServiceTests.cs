using ApexRacers.Api.Services;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Aydsko.iRacingData;
using Aydsko.iRacingData.Common;
using Aydsko.iRacingData.Results;
using NSubstitute;
using Xunit;

namespace ApexRacers.Tests.Services;

public class LapDataServiceTests
{
    private const int SubsessionId = 86109311;
    private const long CustId = 260514;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;


    private static SubsessionLap Lap(int n, double? seconds, bool incident = false) => new()
    {
        LapNumber = n,
        LapTime = seconds is null ? null : TimeSpan.FromSeconds(seconds.Value),
        Incident = incident,
    };

    private static (LapDataService Service, IDataClient Client, AppDbContext Db) Build(
        params SubsessionLap[] laps)
    {
        var client = Substitute.For<IDataClient>();
        client.GetSingleDriverSubsessionLapsAsync(
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new DataResponse<(SubsessionLapsHeader, SubsessionLap[])>
            {
                Data = (new SubsessionLapsHeader(), laps),
            });

        var db = DbContextFactory.Create();
        var cached = new CachedIRacingClient(db, client);
        return (new LapDataService(cached), client, db);
    }

    [Fact]
    public async Task GetDriverLapsAsync_MapsLapsOrdersAndComputesStats()
    {
        var (service, _, db) = Build(
            Lap(3, 61.0),
            Lap(1, 60.0),
            Lap(2, null), // out/incomplete lap — no time
            Lap(4, 60.5));
        await using var _db = db;

        var dto = await service.GetDriverLapsAsync(SubsessionId, CustId, Ct);

        Assert.Equal(SubsessionId, dto.SubsessionId);
        Assert.Equal(CustId, dto.CustId);

        // Ordered by lap number.
        Assert.Equal(new[] { 1, 2, 3, 4 }, dto.Laps.Select(l => l.LapNumber));

        var untimed = dto.Laps.Single(l => l.LapNumber == 2);
        Assert.Equal(-1, untimed.LapTimeSeconds);
        Assert.False(untimed.Valid);

        Assert.True(dto.Laps.Single(l => l.LapNumber == 1).Valid);

        // Stats over the three timed laps (60, 61, 60.5).
        Assert.Equal(60.5, dto.MeanSeconds, precision: 3);
        Assert.Equal(60.0, dto.FastestLapSeconds, precision: 3);
    }

    [Fact]
    public async Task GetDriverLapsAsync_NoLaps_ReturnsEmptyWithZeroStats()
    {
        var (service, _, db) = Build();
        await using var _db = db;

        var dto = await service.GetDriverLapsAsync(SubsessionId, CustId, Ct);

        Assert.Empty(dto.Laps);
        Assert.Equal(0d, dto.FastestLapSeconds);
        Assert.Equal(0d, dto.DegSlopeSecondsPerLap);
    }

    [Fact]
    public async Task GetDriverLapsAsync_SecondCall_ServedFromCache_FetchesOnce()
    {
        var (service, client, db) = Build(Lap(1, 60.0), Lap(2, 60.5));
        await using var _db = db;

        var first = await service.GetDriverLapsAsync(SubsessionId, CustId, Ct);
        var second = await service.GetDriverLapsAsync(SubsessionId, CustId, Ct);

        Assert.Equal(first.Laps.Count, second.Laps.Count);
        Assert.Equal(first.FastestLapSeconds, second.FastestLapSeconds);
        await client.Received(1).GetSingleDriverSubsessionLapsAsync(
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
