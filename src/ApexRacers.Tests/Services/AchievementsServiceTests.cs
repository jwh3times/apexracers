using ApexRacers.Api.Services;
using ApexRacers.Tests.Helpers;
using Aydsko.iRacingData;
using Aydsko.iRacingData.Common;
using Aydsko.iRacingData.Member;
using NSubstitute;
using Xunit;

namespace ApexRacers.Tests.Services;

public class AchievementsServiceTests
{
    private const long CustId = 691062;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;


    private static MemberAward Award(int id, string name, DateOnly date, int count = 1) => new()
    {
        AwardId = id,
        Name = name,
        AwardDate = date,
        AwardCount = count,
        IconUrlLarge = $"https://img/{id}.png",
    };

    private static (AchievementsService Service, IDataClient Client) Build(params MemberAward[] awards)
    {
        var client = Substitute.For<IDataClient>();
        client.GetDriverAwardsAsync(Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new DataResponse<MemberAward[]> { Data = awards });

        var db = DbContextFactory.Create();
        var cached = new CachedIRacingClient(db, client);
        return (new AchievementsService(cached), client);
    }

    [Fact]
    public async Task GetAchievementsAsync_MapsAndOrdersNewestFirst()
    {
        var (service, _) = Build(
            Award(1, "Old Trophy", new DateOnly(2025, 1, 1)),
            Award(2, "New Trophy", new DateOnly(2026, 5, 1), count: 3));

        var dto = await service.GetAchievementsAsync(CustId, Ct);

        Assert.Equal(CustId, dto.CustomerId);
        Assert.Equal(2, dto.AwardCount);
        Assert.Equal(new[] { "New Trophy", "Old Trophy" }, dto.Awards.Select(a => a.Name)); // newest first
        Assert.Equal(3, dto.Awards[0].Count);
        Assert.Equal("https://img/2.png", dto.Awards[0].IconUrl);
    }

    [Fact]
    public async Task GetAchievementsAsync_NoAwards_ReturnsEmpty()
    {
        var (service, _) = Build();

        var dto = await service.GetAchievementsAsync(CustId, Ct);

        Assert.Equal(0, dto.AwardCount);
        Assert.Empty(dto.Awards);
    }

    [Fact]
    public async Task GetAchievementsAsync_SecondCall_ServedFromCache_FetchesOnce()
    {
        var (service, client) = Build(Award(1, "Trophy", new DateOnly(2026, 5, 1)));

        var first = await service.GetAchievementsAsync(CustId, Ct);
        var second = await service.GetAchievementsAsync(CustId, Ct);

        Assert.Equal(first.AwardCount, second.AwardCount);
        Assert.Equal(first.Awards[0].Name, second.Awards[0].Name);
        await client.Received(1).GetDriverAwardsAsync(Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }
}
