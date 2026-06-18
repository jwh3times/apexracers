using System.Text;
using ApexRacers.Api.Services;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Aydsko.iRacingData;
using Aydsko.iRacingData.Stats;
using NSubstitute;
using Xunit;

namespace ApexRacers.Tests.Services;

public class LeaderboardServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Csv =
        "DRIVER,CUSTID,LOCATION,CLUB_NAME,STARTS,WINS,AVG_START_POS,AVG_FINISH_POS,AVG_POINTS,TOP25PCNT,LAPS,LAPSLEAD,AVG_INC,CLASS,IRATING,TTRATING,TOT_CLUBPOINTS,CHAMPPOINTS\n"
        + "Aaron Vazquezz,598355,ES,N/A,1919,694,5,6,154,1267,30143,7800,5.72,A 2.96,11424,2187,20543,295527\n"
        + "Sven Haase,313251,DE,N/A,2403,1399,3,4,183,1911,56705,13214,7.47,A 4.52,11417,1721,33319,440170";

    private sealed class StubServiceProvider(object? service) : IServiceProvider
    {
        public object? GetService(Type serviceType) => service;
    }

    [Fact]
    public async Task GetLeaderboardAsync_DecodesParsesAndCachesOnce()
    {
        await using var db = DbContextFactory.Create();
        var client = Substitute.For<IDataClient>();
        client.GetDriverStatisticsByCategoryCsvAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new DriverStatisticsCsvFile
            {
                CategoryId = 2,
                ContentBytes = Encoding.UTF8.GetBytes(Csv),
            });
        var service = new LeaderboardService(new CachedIRacingClient(db, new StubServiceProvider(client)));

        var first = await service.GetLeaderboardAsync(2, Ct);
        var second = await service.GetLeaderboardAsync(2, Ct);

        Assert.Equal(2, first.Count);
        Assert.Equal("Aaron Vazquezz", first[0].Driver);
        Assert.Equal(1, first[0].Rank);
        Assert.Equal(11424, first[0].IRating);
        Assert.Equal(first.Count, second.Count);

        await client.Received(1).GetDriverStatisticsByCategoryCsvAsync(
            Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetLeaderboardAsync_NullContent_ReturnsEmpty()
    {
        await using var db = DbContextFactory.Create();
        var client = Substitute.For<IDataClient>();
        client.GetDriverStatisticsByCategoryCsvAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new DriverStatisticsCsvFile { CategoryId = 3, ContentBytes = null! });
        var service = new LeaderboardService(new CachedIRacingClient(db, new StubServiceProvider(client)));

        Assert.Empty(await service.GetLeaderboardAsync(3, Ct));
    }
}
