using ApexRacers.Api.Services;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Aydsko.iRacingData;
using Aydsko.iRacingData.Common;
using Aydsko.iRacingData.Member;
using Aydsko.iRacingData.Stats;
using NSubstitute;
using Xunit;

namespace ApexRacers.Tests.Services;

public class DriverStatsServiceTests
{
    private const long CustId = 691062;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;


    private static MemberProfile Profile(params LicenseHistory[] licenses) =>
        new() { LicenseHistory = licenses, CustomerId = (int)CustId };

    private static LicenseHistory License(int categoryId, string category) => new()
    {
        CategoryId = categoryId,
        Category = category,
        Irating = 1500 + categoryId,
        SafetyRating = 2.8f,
        Cpi = 38.7f,
        LicenseLevel = 14,
        GroupName = "Class B",
        TtRating = 1350,
        Color = "00c702",
    };

    private static MemberChart Chart(int categoryId, params (int year, int month, int day, int value)[] pts) =>
        new()
        {
            CategoryId = categoryId,
            ChartType = MemberChartType.IRating,
            Points = pts.Select(p => new MemberChartDataPoint
            {
                Day = new DateOnly(p.year, p.month, p.day),
                Value = p.value,
            }).ToArray(),
        };

    private static (DriverStatsService Service, IDataClient Client, AppDbContext Db) Build(
        MemberProfile profile, params MemberChart[] charts)
    {
        var client = Substitute.For<IDataClient>();
        client.GetMemberProfileAsync(Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new DataResponse<MemberProfile> { Data = profile });
        foreach (var chart in charts)
        {
            client.GetMemberChartDataAsync(
                    Arg.Any<int?>(), chart.CategoryId, MemberChartType.IRating, Arg.Any<CancellationToken>())
                .Returns(new DataResponse<MemberChart> { Data = chart });
        }

        var db = DbContextFactory.Create();
        var cached = new CachedIRacingClient(db, client);
        return (new DriverStatsService(cached), client, db);
    }

    [Fact]
    public async Task GetProgressionAsync_MapsOneCardPerLicense_WithCorrectFields()
    {
        var (service, _, db) = Build(
            Profile(License(1, "oval"), License(5, "sports_car")),
            Chart(1, (2024, 2, 24, 1925)),
            Chart(5, (2024, 1, 1, 1700), (2024, 2, 24, 1854)));
        await using var _db = db;

        var result = await service.GetProgressionAsync(CustId, Ct);

        Assert.Equal(CustId, result.CustomerId);
        Assert.Equal(2, result.Categories.Count);

        var oval = result.Categories.Single(c => c.CategoryId == 1);
        Assert.Equal("Oval", oval.CategoryName);
        Assert.Equal(1501, oval.IRating); // 1500 + categoryId
        Assert.Equal(2.8, oval.SafetyRating, precision: 3);
        Assert.Equal(38.7, oval.Cpi, precision: 3);
        Assert.Equal(14, oval.LicenseLevel);
        Assert.Equal("Class B", oval.GroupName);
        Assert.Equal(1350, oval.TtRating);
        Assert.Equal("00c702", oval.Color);
    }

    [Fact]
    public async Task GetProgressionAsync_PrettifiesMultiWordCategorySlug()
    {
        var (service, _, db) = Build(
            Profile(License(5, "sports_car")),
            Chart(5, (2024, 1, 1, 1700)));
        await using var _db = db;

        var result = await service.GetProgressionAsync(CustId, Ct);

        Assert.Equal("Sports Car", Assert.Single(result.Categories).CategoryName);
    }

    [Fact]
    public async Task GetProgressionAsync_MapsHistoryPointsToIsoDateAndValue()
    {
        var (service, _, db) = Build(
            Profile(License(2, "road")),
            Chart(2, (2024, 1, 3, 1194), (2024, 2, 24, 1925)));
        await using var _db = db;

        var result = await service.GetProgressionAsync(CustId, Ct);
        var history = Assert.Single(result.Categories).IRatingHistory;

        Assert.Equal(2, history.Count);
        Assert.Equal("2024-01-03", history[0].When);
        Assert.Equal(1194, history[0].Value);
        Assert.Equal("2024-02-24", history[1].When);
        Assert.Equal(1925, history[1].Value);
    }

    [Fact]
    public async Task GetProgressionAsync_SecondCall_ServedFromCache_FetchesOnce()
    {
        var (service, client, db) = Build(
            Profile(License(1, "oval")),
            Chart(1, (2024, 2, 24, 1925)));
        await using var _db = db;

        var first = await service.GetProgressionAsync(CustId, Ct);
        var second = await service.GetProgressionAsync(CustId, Ct);

        // Both reads agree (the second deserializes the cached Aydsko payload).
        Assert.Equal(first.Categories.Count, second.Categories.Count);
        Assert.Equal(
            first.Categories[0].IRatingHistory[0].Value,
            second.Categories[0].IRatingHistory[0].Value);

        await client.Received(1).GetMemberProfileAsync(Arg.Any<int?>(), Arg.Any<CancellationToken>());
        await client.Received(1).GetMemberChartDataAsync(
            Arg.Any<int?>(), 1, MemberChartType.IRating, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetProgressionAsync_NoLicenses_ReturnsEmptyCards_WithoutChartFetch()
    {
        var (service, client, db) = Build(Profile());
        await using var _db = db;

        var result = await service.GetProgressionAsync(CustId, Ct);

        Assert.Empty(result.Categories);
        await client.DidNotReceive().GetMemberChartDataAsync(
            Arg.Any<int?>(), Arg.Any<int>(), Arg.Any<MemberChartType>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("oval", "Oval")]
    [InlineData("dirt_oval", "Dirt Oval")]
    [InlineData("formula_car", "Formula Car")]
    [InlineData("road", "Road")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void PrettifyCategory_TitleCasesUnderscoreSlug(string? input, string expected) =>
        Assert.Equal(expected, DriverStatsService.PrettifyCategory(input));

    // ── GetDriverProfileAsync ─────────────────────────────────────────────────

    private static (DriverStatsService Service, IDataClient Client, AppDbContext Db) BuildProfile(
        MemberProfile profile, MemberCareer career, MemberSummary summary, MemberRecap recap)
    {
        var client = Substitute.For<IDataClient>();
        client.GetMemberProfileAsync(Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new DataResponse<MemberProfile> { Data = profile });
        client.GetCareerStatisticsAsync(Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new DataResponse<MemberCareer> { Data = career });
        client.GetMemberSummaryAsync(Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new DataResponse<MemberSummary> { Data = summary });
        client.GetMemberRecapAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new DataResponse<MemberRecap> { Data = recap });

        var db = DbContextFactory.Create();
        var cached = new CachedIRacingClient(db, client);
        return (new DriverStatsService(cached), client, db);
    }

    private static MemberProfile ProfileWithInfo() => new()
    {
        CustomerId = (int)CustId,
        LicenseHistory = [License(5, "sports_car")],
        Info = new MemberProfileInfo
        {
            DisplayName = "Jerry Holland",
            FlairName = "United States",
            FlairShortName = "USA",
            MemberSince = "2021-11-05",
        },
    };

    private static MemberCareer CareerWith(params (int catId, string cat, int starts, int wins)[] rows) =>
        new()
        {
            CustomerId = (int)CustId,
            Statistics = rows.Select(r => new MemberCareerStatistics
            {
                CategoryId = r.catId,
                Category = r.cat,
                Starts = r.starts,
                Wins = r.wins,
                Top5 = 20,
                Poles = 2,
                AvgStartPosition = 9,
                AvgFinishPosition = 10,
                Laps = 2317,
                LapsLed = 99,
                WinPercentage = 8.47f,
                Top5Percentage = 33.9f,
            }).ToArray(),
        };

    private static MemberSummary SummaryWith(int officialSessions, int officialWins) =>
        new()
        {
            CustomerId = (int)CustId,
            YearStatistics = new MemberSummaryYearStatistics
            {
                NumberOfOfficialSessions = officialSessions,
                NumberOfOfficialWins = officialWins,
                NumberOfLeagueSessions = 0,
                NumberOfLeagueWins = 0,
            },
        };

    private static MemberRecap RecapWithFavorites() => new()
    {
        CustomerId = (int)CustId,
        Year = 2026,
        Statistics = new RecapStatistics
        {
            FavoriteCar = new RecapFavoriteCar
            {
                CarId = 119,
                CarName = "Porsche 718 Cayman GT4 Clubsport MR",
                CarImageUrl = new Uri("https://images-static.iracing.com/img/cars/porsche718.jpg"),
            },
            FavoriteTrack = new RecapFavoriteTrack
            {
                TrackId = 249,
                TrackName = "Nürburgring Nordschleife",
                ConfigName = "Industriefahrten",
                TrackLogoUrl = new Uri("https://images-static.iracing.com/img/logos/tracks/nurburgring.png"),
            },
        },
    };

    [Fact]
    public async Task GetDriverProfileAsync_MapsIdentityLicensesCareerSummaryAndFavorites()
    {
        var (service, _, db) = BuildProfile(
            ProfileWithInfo(),
            CareerWith((5, "Sports Car", 94, 4), (2, "Road", 576, 42)),
            SummaryWith(75, 1),
            RecapWithFavorites());
        await using var _db = db;

        var result = await service.GetDriverProfileAsync(CustId, Ct);

        Assert.Equal(CustId, result.CustomerId);
        Assert.Equal("Jerry Holland", result.DisplayName);
        Assert.Equal("United States", result.Country);
        Assert.Equal("USA", result.CountryCode);
        Assert.Equal("2021-11-05", result.MemberSince);

        var badge = Assert.Single(result.Licenses);
        Assert.Equal("Sports Car", badge.CategoryName); // slug prettified
        Assert.Equal(2.8, badge.SafetyRating, precision: 3);

        Assert.Equal(2, result.Career.Count);
        var road = result.Career.Single(c => c.CategoryId == 2);
        Assert.Equal("Road", road.CategoryName); // career category is already display-ready
        Assert.Equal(576, road.Starts);
        Assert.Equal(42, road.Wins);

        Assert.Equal(75, result.ThisYear.OfficialSessions);
        Assert.Equal(1, result.ThisYear.OfficialWins);

        Assert.NotNull(result.FavoriteCar);
        Assert.Equal(119, result.FavoriteCar!.CarId);
        Assert.Equal("Porsche 718 Cayman GT4 Clubsport MR", result.FavoriteCar.CarName);
        Assert.Contains("porsche718", result.FavoriteCar.ImageUrl);

        Assert.NotNull(result.FavoriteTrack);
        Assert.Equal(249, result.FavoriteTrack!.TrackId);
        Assert.Equal("Industriefahrten", result.FavoriteTrack.ConfigName);
    }

    [Fact]
    public async Task GetDriverProfileAsync_NullRecapFavorites_YieldNullChips()
    {
        var recap = new MemberRecap { CustomerId = (int)CustId, Year = 2026, Statistics = null! };
        var (service, _, db) = BuildProfile(
            ProfileWithInfo(), CareerWith((5, "Sports Car", 94, 4)), SummaryWith(10, 0), recap);
        await using var _db = db;

        var result = await service.GetDriverProfileAsync(CustId, Ct);

        Assert.Null(result.FavoriteCar);
        Assert.Null(result.FavoriteTrack);
    }

    [Fact]
    public async Task GetDriverProfileAsync_SecondCall_ServedFromCache_FetchesEachEndpointOnce()
    {
        var (service, client, db) = BuildProfile(
            ProfileWithInfo(), CareerWith((5, "Sports Car", 94, 4)), SummaryWith(75, 1), RecapWithFavorites());
        await using var _db = db;

        await service.GetDriverProfileAsync(CustId, Ct);
        var second = await service.GetDriverProfileAsync(CustId, Ct);

        Assert.Equal("Jerry Holland", second.DisplayName); // deserialized from cache
        await client.Received(1).GetMemberProfileAsync(Arg.Any<int?>(), Arg.Any<CancellationToken>());
        await client.Received(1).GetCareerStatisticsAsync(Arg.Any<int?>(), Arg.Any<CancellationToken>());
        await client.Received(1).GetMemberSummaryAsync(Arg.Any<int?>(), Arg.Any<CancellationToken>());
        await client.Received(1).GetMemberRecapAsync(
            Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    // ── GetComparisonSideAsync ────────────────────────────────────────────────

    private static (DriverStatsService Service, IDataClient Client, AppDbContext Db) BuildSide(
        MemberProfile profile, MemberCareer career, params MemberChart[] charts)
    {
        var client = Substitute.For<IDataClient>();
        client.GetMemberProfileAsync(Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new DataResponse<MemberProfile> { Data = profile });
        client.GetCareerStatisticsAsync(Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new DataResponse<MemberCareer> { Data = career });
        foreach (var chart in charts)
        {
            client.GetMemberChartDataAsync(
                    Arg.Any<int?>(), chart.CategoryId, MemberChartType.IRating, Arg.Any<CancellationToken>())
                .Returns(new DataResponse<MemberChart> { Data = chart });
        }

        var db = DbContextFactory.Create();
        var cached = new CachedIRacingClient(db, client);
        return (new DriverStatsService(cached), client, db);
    }

    [Fact]
    public async Task GetComparisonSideAsync_MapsIdentityLicensesCareerAndIRatingHistory()
    {
        var (service, _, db) = BuildSide(
            ProfileWithInfo(),
            CareerWith((5, "Sports Car", 94, 4)),
            Chart(5, (2024, 1, 1, 1700), (2024, 2, 24, 1854)));
        await using var _db = db;

        var side = await service.GetComparisonSideAsync(CustId, Ct);

        Assert.Equal(CustId, side.CustId);
        Assert.Equal("Jerry Holland", side.DisplayName);
        Assert.Equal("United States", side.Country);
        Assert.Equal("USA", side.CountryCode);
        Assert.Equal("2021-11-05", side.MemberSince);

        var badge = Assert.Single(side.Licenses);
        Assert.Equal("Sports Car", badge.CategoryName); // slug prettified

        var careerCard = Assert.Single(side.Career);
        Assert.Equal(94, careerCard.Starts);
        Assert.Equal(4, careerCard.Wins);

        var hist = Assert.Single(side.IRatingHistory);
        Assert.Equal(5, hist.CategoryId);
        Assert.Equal("Sports Car", hist.CategoryName);
        Assert.Equal(2, hist.Points.Count);
        Assert.Equal("2024-02-24", hist.Points[1].When);
        Assert.Equal(1854, hist.Points[1].Value);
    }

    [Fact]
    public async Task GetComparisonSideAsync_NoLicenses_EmptyHistory_WithoutChartFetch()
    {
        var (service, client, db) = BuildSide(
            new MemberProfile { CustomerId = (int)CustId, LicenseHistory = [], Info = new MemberProfileInfo { DisplayName = "X" } },
            CareerWith((5, "Sports Car", 1, 0)));
        await using var _db = db;

        var side = await service.GetComparisonSideAsync(CustId, Ct);

        Assert.Empty(side.Licenses);
        Assert.Empty(side.IRatingHistory);
        await client.DidNotReceive().GetMemberChartDataAsync(
            Arg.Any<int?>(), Arg.Any<int>(), Arg.Any<MemberChartType>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetComparisonSideAsync_SecondCall_ServedFromCache_FetchesEachOnce()
    {
        var (service, client, db) = BuildSide(
            ProfileWithInfo(), CareerWith((5, "Sports Car", 94, 4)), Chart(5, (2024, 1, 1, 1700)));
        await using var _db = db;

        await service.GetComparisonSideAsync(CustId, Ct);
        await service.GetComparisonSideAsync(CustId, Ct);

        await client.Received(1).GetMemberProfileAsync(Arg.Any<int?>(), Arg.Any<CancellationToken>());
        await client.Received(1).GetCareerStatisticsAsync(Arg.Any<int?>(), Arg.Any<CancellationToken>());
        await client.Received(1).GetMemberChartDataAsync(
            Arg.Any<int?>(), 5, MemberChartType.IRating, Arg.Any<CancellationToken>());
    }
}
