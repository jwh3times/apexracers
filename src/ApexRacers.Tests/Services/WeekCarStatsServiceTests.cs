using System.Text.Json;
using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Seeder.Demo;
using ApexRacers.Tests.Helpers;
using Aydsko.iRacingData;
using DataResponse = Aydsko.iRacingData.Common.DataResponse<(Aydsko.iRacingData.Stats.WorldRecordsHeader, Aydsko.iRacingData.Stats.WorldRecordEntry[])>;
using WorldRecordEntry = Aydsko.iRacingData.Stats.WorldRecordEntry;
using WorldRecordsHeader = Aydsko.iRacingData.Stats.WorldRecordsHeader;
using NSubstitute;
using Xunit;

namespace ApexRacers.Tests.Services;

public class WeekCarStatsServiceTests
{
    private static (Series series, Season season, Week week, Car car, CarClass carClass, Subsession subsession) SeedBasic(AppDbContext db)
    {
        var series = new Series { Id = 1, Name = "GT3 Cup" };
        var season = new Season { Id = 1, SeriesId = 1, Year = 2026, Quarter = 2, Active = true, Series = series };
        var track = new Track { Id = 99, Name = "Spa", ConfigName = "Full" };
        var week = new Week { Id = Guid.NewGuid(), SeasonId = 1, RaceWeekIndex = 1, StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), TrackId = 99, Track = track, Season = season };
        var car = new Car { Id = 1, Name = "Porsche 992 GT3", NameAbbreviated = "P992" };
        var carClass = new CarClass { Id = 1, Name = "GT3", ShortName = "GT3", RelativeSpeed = 52 };
        var subsession = new Subsession { Id = -1, SeasonId = 1, RaceWeekIndex = 1, WeekId = week.Id, TrackId = 99, StartTime = DateTimeOffset.UtcNow.AddHours(-2) };
        db.Series.Add(series);
        db.Seasons.Add(season);
        db.Tracks.Add(track);
        db.Weeks.Add(week);
        db.Cars.Add(car);
        db.CarClasses.Add(carClass);
        db.Subsessions.Add(subsession);
        return (series, season, week, car, carClass, subsession);
    }

    private static void AddResult(AppDbContext db, Subsession subsession, Car car, CarClass carClass, long custId, double lapSeconds)
    {
        db.SubsessionResults.Add(new SubsessionResult
        {
            SubsessionId            = subsession.Id,
            CustId                  = custId,
            CarId                   = car.Id,
            CarClassId              = carClass.Id,
            BestLapSeconds          = lapSeconds,
            AverageLapSeconds       = lapSeconds * 1.01,
            FinishPosition          = 0,
            FinishPositionInClass   = 0,
            StartingPosition        = 0,
            StartingPositionInClass = 0,
            Incidents               = 0,
            LapsComplete            = 5,
            LapsLead                = 0,
            ChampPoints             = 0,
            AggregateChampPoints    = 0,
            NewIRating              = 1500,
            OldIRating              = 1500,
            NewCpi                  = 2.0,
            OldCpi                  = 2.0,
            ReasonOutId             = 0,
            Division                = 1,
        });
    }

    [Fact]
    public async Task GetCarsForWeekAsync_WeekNotInSeries_ReturnsEmpty()
    {
        await using var db = DbContextFactory.Create();
        SeedBasic(db);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new WeekCarStatsService(db).GetCarsForWeekAsync(seriesId: 99, raceWeekIndex: 1, TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetCarsForWeekAsync_OddLapCount_ReturnsMiddleValueAsMedian()
    {
        await using var db = DbContextFactory.Create();
        var (_, _, week, car, carClass, subsession) = SeedBasic(db);
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 60);
        AddResult(db, subsession, car, carClass, custId: 2, lapSeconds: 70);
        AddResult(db, subsession, car, carClass, custId: 3, lapSeconds: 90);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new WeekCarStatsService(db).GetCarsForWeekAsync(seriesId: 1, raceWeekIndex: 1, TestContext.Current.CancellationToken);

        var dto = Assert.Single(result);
        Assert.Equal(3, dto.EntryCount);
        Assert.Equal(60, dto.FastestLapSeconds);
        Assert.Equal(70, dto.MedianLapSeconds);
    }

    [Fact]
    public async Task GetCarsForWeekAsync_EvenLapCount_ReturnsAverageOfTwoMiddleValuesAsMedian()
    {
        await using var db = DbContextFactory.Create();
        var (_, _, week, car, carClass, subsession) = SeedBasic(db);
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 60);
        AddResult(db, subsession, car, carClass, custId: 2, lapSeconds: 70);
        AddResult(db, subsession, car, carClass, custId: 3, lapSeconds: 80);
        AddResult(db, subsession, car, carClass, custId: 4, lapSeconds: 90);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new WeekCarStatsService(db).GetCarsForWeekAsync(seriesId: 1, raceWeekIndex: 1, TestContext.Current.CancellationToken);

        var dto = Assert.Single(result);
        Assert.Equal(75, dto.MedianLapSeconds);
    }

    [Fact]
    public async Task GetCarsForWeekAsync_IncludesClassName()
    {
        await using var db = DbContextFactory.Create();
        var (_, _, _, car, carClass, subsession) = SeedBasic(db);
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 60);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new WeekCarStatsService(db).GetCarsForWeekAsync(seriesId: 1, raceWeekIndex: 1, TestContext.Current.CancellationToken);

        var dto = Assert.Single(result);
        Assert.Equal("GT3", dto.ClassName);
    }

    [Fact]
    public async Task GetWeekDetailAsync_ReturnsSeriesNameAndTrackInfo()
    {
        await using var db = DbContextFactory.Create();
        var (_, _, _, car, carClass, subsession) = SeedBasic(db);
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 90);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new WeekCarStatsService(db).GetWeekDetailAsync(seriesId: 1, raceWeekIndex: 1, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("GT3 Cup", result.SeriesName);
        Assert.Equal("Spa", result.TrackName);
        Assert.Equal("Full", result.TrackConfigName);
        Assert.Single(result.Cars);
    }

    [Fact]
    public async Task GetWeekDetailAsync_UnknownSeries_ReturnsNull()
    {
        await using var db = DbContextFactory.Create();
        SeedBasic(db);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new WeekCarStatsService(db).GetWeekDetailAsync(seriesId: 99, raceWeekIndex: 1, TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCarsForWeekAsync_MultipleCars_OrderedByFastestLapAscending()
    {
        await using var db = DbContextFactory.Create();
        var (_, _, week, car, carClass, subsession) = SeedBasic(db);
        var car2 = new Car { Id = 2, Name = "Ferrari 296 GT3", NameAbbreviated = "F296" };
        db.Cars.Add(car2);
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 70);
        AddResult(db, subsession, car2, carClass, custId: 2, lapSeconds: 60);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new WeekCarStatsService(db).GetCarsForWeekAsync(seriesId: 1, raceWeekIndex: 1, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result[0].CarId); // Ferrari is faster
        Assert.Equal(1, result[1].CarId);
    }

    // ── World-record overlay on Week Detail ───────────────────────────────────

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static void SeedWorldRecord(AppDbContext db, int carId, int trackId, double seconds) =>
        db.ExternalDataCaches.Add(new ExternalDataCache
        {
            CacheKey = IRacingCacheKeys.WorldRecord(carId, trackId).Key,
            Payload = JsonSerializer.Serialize<double?>(seconds),
            FetchedAt = DateTimeOffset.UtcNow,
            ExpiresAt = new DateTimeOffset(9999, 1, 1, 0, 0, 0, TimeSpan.Zero),
        });

    private static WeekCarStatsService WithWorldRecords(AppDbContext db, IDataClient? client = null) =>
        new(db, new WorldRecordService(new MappingEvidenceCache(db, client)));

    [Fact]
    public async Task GetWeekDetailAsync_CachedWorldRecord_ReturnsRecordAndFieldGap()
    {
        await using var db = DbContextFactory.Create();
        var (_, _, _, car, carClass, subsession) = SeedBasic(db);
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 70.25);
        AddResult(db, subsession, car, carClass, custId: 2, lapSeconds: 72.0);
        SeedWorldRecord(db, carId: 1, trackId: 99, seconds: 66.5);
        await db.SaveChangesAsync(Ct);

        var result = await WithWorldRecords(db).GetWeekDetailAsync(seriesId: 1, raceWeekIndex: 1, Ct);

        var dto = Assert.Single(result!.Cars);
        Assert.Equal(66.5, dto.WorldRecordLapSeconds);
        Assert.Equal(3.75, dto.WorldRecordGapSeconds); // Field fastest 70.25 − record 66.5
    }

    [Fact]
    public async Task GetWeekDetailAsync_NoRecordAndIRacingNotConfigured_LeavesRecordAndGapNull()
    {
        await using var db = DbContextFactory.Create();
        var (_, _, _, car, carClass, subsession) = SeedBasic(db);
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 70);
        await db.SaveChangesAsync(Ct);

        var result = await WithWorldRecords(db, client: null).GetWeekDetailAsync(seriesId: 1, raceWeekIndex: 1, Ct);

        var dto = Assert.Single(result!.Cars);
        Assert.Equal(70, dto.FastestLapSeconds);
        Assert.Null(dto.WorldRecordLapSeconds);
        Assert.Null(dto.WorldRecordGapSeconds);
    }

    [Fact]
    public async Task GetWeekDetailAsync_RecordLookupThrows_ThatCarAndLaterCarsGetNoRecordAndNoLookup()
    {
        // A failed cache write can leave entities tracked on the request's shared context, so the
        // service stops looking up after the first failure rather than touching that context again.
        await using var db = DbContextFactory.Create();
        var (_, _, _, car, carClass, subsession) = SeedBasic(db);
        var failing = new Car { Id = 2, Name = "Ferrari 296 GT3", NameAbbreviated = "F296" };
        var later = new Car { Id = 3, Name = "BMW M4 GT3", NameAbbreviated = "M4" };
        db.Cars.AddRange(failing, later);
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 70);
        AddResult(db, subsession, failing, carClass, custId: 2, lapSeconds: 71);
        AddResult(db, subsession, later, carClass, custId: 3, lapSeconds: 72);
        await db.SaveChangesAsync(Ct);

        var client = Substitute.For<IDataClient>();
        client.GetWorldRecordsAsync(1, 99, Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new DataResponse
            {
                Data = (new WorldRecordsHeader(), [new WorldRecordEntry { QualifyLapTime = TimeSpan.FromSeconds(68) }]),
            });
        client.GetWorldRecordsAsync(2, 99, Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns<DataResponse>(
                _ => throw new HttpRequestException("iRacing unavailable"));
        client.GetWorldRecordsAsync(3, 99, Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new DataResponse
            {
                Data = (new WorldRecordsHeader(), [new WorldRecordEntry { QualifyLapTime = TimeSpan.FromSeconds(69) }]),
            });

        var result = await WithWorldRecords(db, client).GetWeekDetailAsync(seriesId: 1, raceWeekIndex: 1, Ct);

        Assert.NotNull(result);
        var ok = Assert.Single(result.Cars, c => c.CarId == 1);
        Assert.Equal(68, ok.WorldRecordLapSeconds);
        Assert.Equal(2, ok.WorldRecordGapSeconds);
        var failed = Assert.Single(result.Cars, c => c.CarId == 2);
        Assert.Equal(71, failed.FastestLapSeconds);
        Assert.Null(failed.WorldRecordLapSeconds);
        Assert.Null(failed.WorldRecordGapSeconds);
        var skipped = Assert.Single(result.Cars, c => c.CarId == 3);
        Assert.Equal(72, skipped.FastestLapSeconds);
        Assert.Null(skipped.WorldRecordLapSeconds);
        Assert.Null(skipped.WorldRecordGapSeconds);
        await client.DidNotReceive().GetWorldRecordsAsync(
            3, Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetWeekDetailAsync_FieldFasterThanStaleRecord_ReturnsNegativeGapRoundedToFourPlaces()
    {
        await using var db = DbContextFactory.Create();
        var (_, _, _, car, carClass, subsession) = SeedBasic(db);
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 65.87654);
        SeedWorldRecord(db, carId: 1, trackId: 99, seconds: 66.0);
        await db.SaveChangesAsync(Ct);

        var result = await WithWorldRecords(db).GetWeekDetailAsync(seriesId: 1, raceWeekIndex: 1, Ct);

        Assert.Equal(-0.1235, Assert.Single(result!.Cars).WorldRecordGapSeconds);
    }

    [Fact]
    public async Task GetCarsForWeekAsync_MakesNoWorldRecordLookups()
    {
        await using var db = DbContextFactory.Create();
        var (_, _, _, car, carClass, subsession) = SeedBasic(db);
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 70);
        await db.SaveChangesAsync(Ct);
        var client = Substitute.For<IDataClient>();

        var result = await WithWorldRecords(db, client).GetCarsForWeekAsync(seriesId: 1, raceWeekIndex: 1, Ct);

        Assert.Single(result);
        await client.DidNotReceiveWithAnyArgs().GetWorldRecordsAsync(default, default, default, default, Ct);
    }

    [Fact]
    public async Task GetWeekDetailAsync_DemoSurfaceSeeded_EveryCarShowsARecordAndPositiveGap()
    {
        // End to end over the Demo surface: the seeder's world-record rows read back through the
        // runtime CachedIRacingClient, with no iRacing client configured.
        await using var db = DbContextFactory.Create(ApexRacers.Core.DataProvenance.Demo);
        var (_, _, _, car, carClass, subsession) = SeedBasic(db);
        var second = new Car { Id = 2, Name = "Ferrari 296 GT3", NameAbbreviated = "F296" };
        db.Cars.Add(second);
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 100.0);
        AddResult(db, subsession, car, carClass, custId: 2, lapSeconds: 101.0);
        AddResult(db, subsession, second, carClass, custId: 3, lapSeconds: 102.0);
        await db.SaveChangesAsync(Ct);
        await new DemoCacheSeeder(db).SeedWorldRecordsAsync(Ct);

        var service = new WeekCarStatsService(db, new WorldRecordService(new CachedIRacingClient(db, client: null)));
        var result = await service.GetWeekDetailAsync(seriesId: 1, raceWeekIndex: 1, Ct);

        Assert.Equal(2, result!.Cars.Count);
        var first = Assert.Single(result.Cars, c => c.CarId == 1);
        Assert.Equal(98.0, first.WorldRecordLapSeconds!.Value, precision: 4); // 100.0 × 0.98
        Assert.Equal(2.0, first.WorldRecordGapSeconds!.Value, precision: 4);
        Assert.All(result.Cars, c => Assert.True(c.WorldRecordGapSeconds > 0));
    }
}
