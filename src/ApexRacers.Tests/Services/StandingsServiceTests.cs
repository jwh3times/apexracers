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

public class StandingsServiceTests
{
    private const int SeriesId = 444;
    private const int SeasonId = 6115;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;


    private static SeasonDriverStanding Standing(
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

    private static SeasonTimeTrialStanding TtStanding(
        int rank, int custId, string name, int points, int ttRating) => new()
    {
        Rank = rank,
        CustomerId = custId,
        DisplayName = name,
        Division = 2,
        License = new License { TimeTrialRating = ttRating, iRating = 3000 },
        WeeksCounted = 8,
        Starts = 12,
        Wins = 4,
        Top5 = 7,
        Poles = 1,
        AverageStartPosition = 6.1m,
        AverageFinishPosition = 5.5m,
        Incidents = 20,
        Points = points,
        RawPoints = points,
        WeekDropped = false,
    };

    private sealed class Harness
    {
        public required StandingsService Service { get; init; }
        public required IDataClient Client { get; init; }
        public required IChunkDownloader Downloader { get; init; }
        public required AppDbContext Db { get; init; }
    }

    private static Harness Build(
        bool activeSeason = true,
        bool withClasses = true,
        bool withWeeks = true,
        SeasonDriverStanding[]? standings = null,
        SeasonTimeTrialStanding[]? ttStandings = null,
        ChunkInfo? qualifyChunkInfo = null,
        IReadOnlyList<string>? qualifyChunks = null)
    {
        var db = DbContextFactory.Create();
        db.Series.Add(new Series { Id = SeriesId, Name = "GT3 Cup" });
        db.Seasons.Add(new Season { Id = SeasonId, SeriesId = SeriesId, Active = activeSeason, Year = 2026, Quarter = 2 });
        if (withClasses)
        {
            db.CarClasses.Add(new Core.Models.CarClass { Id = 4091, Name = "GT3 Class", ShortName = "GT3", RelativeSpeed = 50 });
            db.CarClasses.Add(new Core.Models.CarClass { Id = 2000, Name = "GT4 Class", ShortName = "GT4", RelativeSpeed = 40 });
            db.SeasonCarClasses.Add(new SeasonCarClass { SeasonId = SeasonId, CarClassId = 4091 });
            db.SeasonCarClasses.Add(new SeasonCarClass { SeasonId = SeasonId, CarClassId = 2000 });
        }
        if (withWeeks)
        {
            // All start dates in the past so the "current week" default resolves to the max (2).
            for (var w = 0; w <= 2; w++)
                db.Weeks.Add(new Week
                {
                    Id = Guid.NewGuid(),
                    SeasonId = SeasonId,
                    WeekNumber = w,
                    TrackId = 100 + w,
                    StartDate = new DateOnly(2026, 1, 1).AddDays(w * 7),
                });
        }
        db.SaveChanges();

        var client = Substitute.For<IDataClient>();
        client.GetSeasonDriverStandingsAsync(
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new DataResponse<(SeasonDriverStandingsHeader, SeasonDriverStanding[])>
            {
                Data = (new SeasonDriverStandingsHeader(), standings ?? []),
            });

        client.GetSeasonTimeTrialStandingsAsync(
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new DataResponse<(SeasonTimeTrialStandingsHeader, SeasonTimeTrialStanding[])>
            {
                Data = (new SeasonTimeTrialStandingsHeader(), ttStandings ?? []),
            });

        client.GetSeasonQualifyResultsAsync(
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new DataResponse<(SeasonQualifyResultsHeader, SeasonQualifyResult[])>
            {
                Data = (new SeasonQualifyResultsHeader { ChunkInfo = qualifyChunkInfo! }, []),
            });

        var downloader = Substitute.For<IChunkDownloader>();
        downloader.DownloadAsync(Arg.Any<string>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(qualifyChunks ?? []);

        var cached = new CachedIRacingClient(db, client);
        return new Harness
        {
            Service = new StandingsService(db, cached, downloader),
            Client = client,
            Downloader = downloader,
            Db = db,
        };
    }

    // ----- Driver standings (existing behavior) -----

    [Fact]
    public async Task GetDriverStandingsAsync_DefaultsToFirstClass_MapsAndOrders()
    {
        var h = Build(standings: [Standing(2, 222, "Second", 880, 3), Standing(1, 111, "Leader", 950, 7)]);
        await using var _db = h.Db;

        var dto = await h.Service.GetDriverStandingsAsync(SeriesId, carClassId: null, Ct);

        Assert.Equal("GT3 Cup", dto.SeriesName);
        Assert.Equal(4091, dto.CarClassId);
        Assert.Equal("GT3 Class", dto.CarClassName);
        Assert.Equal(2, dto.CarClasses.Count);
        Assert.Equal(new[] { 1, 2 }, dto.Standings.Select(s => s.Standing));
        Assert.Equal("Leader", dto.Standings[0].DriverName);
        Assert.Equal(950, dto.Standings[0].Points);
        Assert.Equal(5.4, dto.Standings[0].AvgFinishPosition, precision: 3);
    }

    [Fact]
    public async Task GetDriverStandingsAsync_RespectsRequestedClass()
    {
        var h = Build(standings: [Standing(1, 111, "Leader", 950, 7)]);
        await using var _db = h.Db;

        var dto = await h.Service.GetDriverStandingsAsync(SeriesId, carClassId: 2000, Ct);

        Assert.Equal(2000, dto.CarClassId);
        Assert.Equal("GT4 Class", dto.CarClassName);
    }

    [Fact]
    public async Task GetDriverStandingsAsync_SecondCall_ServedFromCache_FetchesOnce()
    {
        var h = Build(standings: [Standing(1, 111, "Leader", 950, 7)]);
        await using var _db = h.Db;

        await h.Service.GetDriverStandingsAsync(SeriesId, null, Ct);
        var second = await h.Service.GetDriverStandingsAsync(SeriesId, null, Ct);

        Assert.Single(second.Standings);
        await h.Client.Received(1).GetSeasonDriverStandingsAsync(
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetDriverStandingsAsync_SeasonNeitherBegunNorActive_ThrowsKeyNotFound()
    {
        // No race week has begun, so there is nothing to be "most recently begun"; with the active
        // flag clear too, the fallback has no candidate either.
        var h = Build(activeSeason: false, withWeeks: false, standings: [Standing(1, 111, "Leader", 950, 7)]);
        await using var _db = h.Db;

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => h.Service.GetDriverStandingsAsync(SeriesId, null, Ct));
    }

    [Fact]
    public async Task GetDriverStandingsAsync_SeasonDeactivatedUpstreamButAlreadyRaced_StillServesIt()
    {
        // Standings for the season drivers just finished must survive the active flag clearing
        // during the inter-season gap.
        var h = Build(activeSeason: false, standings: [Standing(1, 111, "Leader", 950, 7)]);
        await using var _db = h.Db;

        var dto = await h.Service.GetDriverStandingsAsync(SeriesId, null, Ct);

        Assert.Equal("Leader", Assert.Single(dto.Standings).DriverName);
    }

    [Fact]
    public async Task GetDriverStandingsAsync_NoCarClasses_ThrowsKeyNotFound()
    {
        var h = Build(withClasses: false, standings: [Standing(1, 111, "Leader", 950, 7)]);
        await using var _db = h.Db;

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => h.Service.GetDriverStandingsAsync(SeriesId, null, Ct));
    }

    // ----- Time Trial standings -----

    [Fact]
    public async Task GetTimeTrialStandingsAsync_MapsOrdersAndIncludesTtRating()
    {
        var h = Build(ttStandings: [TtStanding(2, 222, "Second", 700, 2100), TtStanding(1, 111, "Leader", 800, 2500)]);
        await using var _db = h.Db;

        var dto = await h.Service.GetTimeTrialStandingsAsync(SeriesId, carClassId: null, Ct);

        Assert.Equal(4091, dto.CarClassId);
        Assert.Equal(new[] { 1, 2 }, dto.Standings.Select(s => s.Standing));
        Assert.Equal("Leader", dto.Standings[0].DriverName);
        Assert.Equal(2500, dto.Standings[0].TtRating);
        Assert.Equal(800, dto.Standings[0].Points);
        Assert.Equal(5.5, dto.Standings[0].AvgFinishPosition, precision: 3);
        Assert.Equal(2, dto.Standings[0].Division);
    }

    [Fact]
    public async Task GetTimeTrialStandingsAsync_RespectsRequestedClass_AndCachesPerClass()
    {
        var h = Build(ttStandings: [TtStanding(1, 111, "Leader", 800, 2500)]);
        await using var _db = h.Db;

        await h.Service.GetTimeTrialStandingsAsync(SeriesId, carClassId: 2000, Ct);
        var second = await h.Service.GetTimeTrialStandingsAsync(SeriesId, carClassId: 2000, Ct);

        Assert.Equal(2000, second.CarClassId);
        await h.Client.Received(1).GetSeasonTimeTrialStandingsAsync(
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    // ----- Qualifying results -----

    private static ChunkInfo Ci() => new()
    {
        BaseDownloadUrl = "https://s3/base/",
        ChunkFileNames = ["c1.json"],
        NumberOfChunks = 1,
        Rows = 2,
        ChunkSize = 500,
    };

    private const string QualChunk = """
    [
      { "rank": 2, "cust_id": 222, "display_name": "Second", "division": 3,
        "license": { "irating": 5000 }, "best_qual_lap_time": 3760000, "week": 2 },
      { "rank": 1, "cust_id": 111, "display_name": "Pole", "division": 2,
        "license": { "irating": 7000 }, "best_qual_lap_time": 3750000, "week": 2 }
    ]
    """;

    [Fact]
    public async Task GetQualifyResultsAsync_DownloadsChunks_ParsesOrdersAndDefaultsToCurrentWeek()
    {
        var h = Build(qualifyChunkInfo: Ci(), qualifyChunks: [QualChunk]);
        await using var _db = h.Db;

        var dto = await h.Service.GetQualifyResultsAsync(SeriesId, carClassId: null, raceWeekNum: null, Ct);

        Assert.Equal(4091, dto.CarClassId);
        Assert.Equal(2, dto.RaceWeekNum); // latest past week
        Assert.Equal(new[] { 0, 1, 2 }, dto.AvailableWeeks);
        Assert.Equal(new[] { 1, 2 }, dto.Results.Select(r => r.Standing));
        Assert.Equal("Pole", dto.Results[0].DriverName);
        Assert.Equal(375.0, dto.Results[0].BestQualLapSeconds, precision: 4);
        Assert.Equal(7000, dto.Results[0].IRating);
    }

    [Fact]
    public async Task GetQualifyResultsAsync_RespectsExplicitWeek()
    {
        var h = Build(qualifyChunkInfo: Ci(), qualifyChunks: [QualChunk]);
        await using var _db = h.Db;

        var dto = await h.Service.GetQualifyResultsAsync(SeriesId, carClassId: null, raceWeekNum: 1, Ct);

        Assert.Equal(1, dto.RaceWeekNum);
    }

    [Fact]
    public async Task GetQualifyResultsAsync_NoChunkInfo_ReturnsEmpty_WithoutDownloading()
    {
        var h = Build(qualifyChunkInfo: null, qualifyChunks: [QualChunk]);
        await using var _db = h.Db;

        var dto = await h.Service.GetQualifyResultsAsync(SeriesId, carClassId: null, raceWeekNum: 0, Ct);

        Assert.Empty(dto.Results);
        await h.Downloader.DidNotReceive().DownloadAsync(
            Arg.Any<string>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>());
    }
}
