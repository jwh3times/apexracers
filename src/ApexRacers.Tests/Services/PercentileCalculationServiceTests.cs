using ApexRacers.Api.Services;
using ApexRacers.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Aydsko.iRacingData;
using NSubstitute;
using Xunit;

namespace ApexRacers.Tests.Services;

public class PercentileCalculationServiceTests
{
    private static PercentileCalculationService CreateService(AppDbContext db, WorldRecordService? worldRecords = null) =>
        new(db, new SubjectDriverContext(db, new FeatureFlagEligibility(db)), worldRecords);

    private static PersonalBestEvidence OfficialEvidence => PersonalBestEvidence.OfficialRaceLapsOnly;

    private static (Week week, Car car, CarClass carClass, Subsession subsession) SeedWeekAndCar(AppDbContext db)
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
        return (week, car, carClass, subsession);
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
    public async Task ComputeAndCacheAsync_DriverHasNoLapTime_ReturnsNull()
    {
        await using var db = DbContextFactory.Create();
        var (week, car, carClass, subsession) = SeedWeekAndCar(db);
        AddResult(db, subsession, car, carClass, custId: 999, lapSeconds: 70);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateService(db).ComputeAndCacheAsync(seriesId: 1, raceWeekIndex: 1, carId: 1, customerId: 1, evidence: OfficialEvidence, ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task ComputeAndCacheAsync_OnlyDriverInField_IsThatFieldsMedian()
    {
        await using var db = DbContextFactory.Create();
        var (week, car, carClass, subsession) = SeedWeekAndCar(db);
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 70);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateService(db).ComputeAndCacheAsync(seriesId: 1, raceWeekIndex: 1, carId: 1, customerId: 1, evidence: OfficialEvidence, ct: TestContext.Current.CancellationToken);

        // A Field of one: (0 slower + 0.5 x 1 tied) / 1. Alone, the driver is its median —
        // reporting 100 would claim they had beaten a field that does not exist.
        Assert.NotNull(result);
        Assert.Equal(50.0, result.PercentileRank);
        Assert.Equal(1, result.SampleSize);
        Assert.Equal(1, result.FieldPosition);
        Assert.Equal(100, result.TopSharePercent);
        Assert.False(result.IsPercentilePresentable);
    }

    [Fact]
    public async Task ComputeAndCacheAsync_DriverBeats3Of4Others_Returns70Percentile()
    {
        await using var db = DbContextFactory.Create();
        var (week, car, carClass, subsession) = SeedWeekAndCar(db);
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 65);
        AddResult(db, subsession, car, carClass, custId: 2, lapSeconds: 60);
        AddResult(db, subsession, car, carClass, custId: 3, lapSeconds: 70);
        AddResult(db, subsession, car, carClass, custId: 4, lapSeconds: 80);
        AddResult(db, subsession, car, carClass, custId: 5, lapSeconds: 90);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateService(db).ComputeAndCacheAsync(seriesId: 1, raceWeekIndex: 1, carId: 1, customerId: 1, evidence: OfficialEvidence, ct: TestContext.Current.CancellationToken);

        // Field of 5: (3 slower + 0.5 x 1 tied) / 5 = 70. Second of five is the top 40%.
        Assert.NotNull(result);
        Assert.Equal(70.0, result.PercentileRank);
        Assert.Equal(5, result.SampleSize);
        Assert.Equal(2, result.FieldPosition);
        Assert.Equal(40, result.TopSharePercent);
        Assert.True(result.IsPercentilePresentable);
    }

    [Fact]
    public async Task ComputeAndCacheAsync_UserProfileExists_CreatesCarPercentileResult()
    {
        await using var db = DbContextFactory.Create();
        var (week, car, carClass, subsession) = SeedWeekAndCar(db);
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Jerry" });
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 70);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await CreateService(db).ComputeAndCacheAsync(seriesId: 1, raceWeekIndex: 1, carId: 1, customerId: 1, evidence: OfficialEvidence, callerUserId: userId, ct: TestContext.Current.CancellationToken);

        var cached = Assert.Single(db.CarPercentileResults);
        Assert.Equal(userId, cached.UserId);
        Assert.Equal(50, cached.PercentileRank);
        Assert.Equal(100, cached.TopSharePercent);
        Assert.Equal(1, cached.SampleSize);
    }

    [Fact]
    public async Task ComputeAndCacheAsync_CachedResultExists_UpdatesExistingRow()
    {
        await using var db = DbContextFactory.Create();
        var (week, car, carClass, subsession) = SeedWeekAndCar(db);
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Jerry" });
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 70);
        var oldTime = DateTimeOffset.UtcNow.AddDays(-1);
        db.CarPercentileResults.Add(new CarPercentileResult { UserId = userId, CarId = 1, SeriesId = 1, WeekId = week.Id, PercentileRank = 25, SampleSize = 2, ComputedAt = oldTime });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await CreateService(db).ComputeAndCacheAsync(seriesId: 1, raceWeekIndex: 1, carId: 1, customerId: 1, evidence: OfficialEvidence, callerUserId: userId, ct: TestContext.Current.CancellationToken);

        Assert.Single(db.CarPercentileResults);
        Assert.Equal(50.0, db.CarPercentileResults.Single().PercentileRank);
        Assert.Equal(100, db.CarPercentileResults.Single().TopSharePercent);
        Assert.Equal(1, db.CarPercentileResults.Single().SampleSize);
        Assert.True(db.CarPercentileResults.Single().ComputedAt > oldTime);
    }

    [Fact]
    public async Task ComputeAndCacheAsync_NoUserProfile_DoesNotCreateCacheRow()
    {
        await using var db = DbContextFactory.Create();
        var (week, car, carClass, subsession) = SeedWeekAndCar(db);
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 70);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await CreateService(db).ComputeAndCacheAsync(seriesId: 1, raceWeekIndex: 1, carId: 1, customerId: 1, evidence: OfficialEvidence, ct: TestContext.Current.CancellationToken);

        Assert.Empty(db.CarPercentileResults);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ComputeAndCacheAsync_ForeignDriver_DoesNotInsertOrChangeCallerCache(bool existingCache)
    {
        await using var db = DbContextFactory.Create();
        var (week, car, carClass, subsession) = SeedWeekAndCar(db);
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Caller" });
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 90);
        AddResult(db, subsession, car, carClass, custId: 2, lapSeconds: 70);
        var previousTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        if (existingCache)
            db.CarPercentileResults.Add(new CarPercentileResult
            {
                UserId = userId, CarId = 1, SeriesId = 1, WeekId = week.Id,
                PercentileRank = 25, TopSharePercent = 100, SampleSize = 12, ComputedAt = previousTime,
            });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateService(db).ComputeAndCacheAsync(1, 1, 1, 2, OfficialEvidence, userId,
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(2, result.CustomerId);
        Assert.Equal(70, result.YourBestLapSeconds);
        Assert.Equal(75, result.PercentileRank);
        var rows = await db.CarPercentileResults.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
        if (existingCache)
        {
            var row = Assert.Single(rows);
            Assert.Equal(userId, row.UserId);
            Assert.Equal(25, row.PercentileRank);
            Assert.Equal(100, row.TopSharePercent);
            Assert.Equal(12, row.SampleSize);
            Assert.Equal(previousTime, row.ComputedAt);
        }
        else
            Assert.Empty(rows);
    }

    [Theory]
    [InlineData("unlinked")]
    [InlineData("unknown")]
    [InlineData("absent")]
    public async Task ComputeAndCacheAsync_NoCallerSubject_ReturnsLookupWithoutCaching(string callerState)
    {
        await using var db = DbContextFactory.Create();
        var (_, car, carClass, subsession) = SeedWeekAndCar(db);
        Guid? userId = callerState == "absent" ? null : Guid.NewGuid();
        if (callerState == "unlinked")
            db.Users.Add(new ApplicationUser { Id = userId!.Value, DisplayName = "Unlinked" });
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 70);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateService(db).ComputeAndCacheAsync(1, 1, 1, 1, OfficialEvidence, userId,
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(70, result.YourBestLapSeconds);
        Assert.Empty(await db.CarPercentileResults.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("Alpha", true, true, true, 1L)]
    [InlineData("Alpha", true, false, false, 1L)]
    [InlineData("Standard", true, true, false, 1L)]
    [InlineData("Standard", true, false, true, 1L)]
    [InlineData("Alpha", false, true, false, 1L)]
    [InlineData("Alpha", false, false, true, 1L)]
    [InlineData("Alpha", true, true, true, null)]
    public async Task ComputeAndCacheAsync_DemoEligibility_CachesOnlyResolvedSubject(
        string roleName, bool demoEnabled, bool lookupDemo, bool shouldCache, long? claimedCustomerId)
    {
        await using var db = DbContextFactory.Create();
        var (_, car, carClass, subsession) = SeedWeekAndCar(db);
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, DisplayName = "Caller", IRacingCustomerId = claimedCustomerId });
        db.Roles.Add(new IdentityRole<Guid> { Id = roleId, Name = roleName, NormalizedName = roleName.ToUpperInvariant() });
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = userId, RoleId = roleId });
        db.FeatureFlags.Add(new FeatureFlag
        {
            Key = "iracing-demo", Name = "Demo", MinimumRole = "Alpha", IsEnabled = demoEnabled,
        });
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 90);
        AddResult(db, subsession, car, carClass, custId: DemoData.DriverCustId, lapSeconds: 70);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var customerId = lookupDemo ? DemoData.DriverCustId : 1;

        var result = await CreateService(db).ComputeAndCacheAsync(1, 1, 1, customerId, OfficialEvidence, userId,
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(customerId, result.CustomerId);
        var rows = await db.CarPercentileResults.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
        if (shouldCache)
        {
            var row = Assert.Single(rows);
            Assert.Equal(userId, row.UserId);
            Assert.Equal(result.PercentileRank, row.PercentileRank);
        }
        else
            Assert.Empty(rows);
    }

    [Fact]
    public async Task ComputeAndCacheAsync_UploadedEvidenceCannotOverlayDifferentSubjectDriver()
    {
        await using var db = DbContextFactory.Create();
        var (week, car, carClass, subsession) = SeedWeekAndCar(db);
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, IRacingCustomerId = 1, DisplayName = "Caller" });
        AddResult(db, subsession, car, carClass, custId: 2, lapSeconds: 80);
        AddResult(db, subsession, car, carClass, custId: 3, lapSeconds: 90);
        db.UploadedLaps.Add(new UploadedLap
        {
            UserId = userId,
            CarId = car.Id,
            TrackId = week.TrackId,
            LapTimeSeconds = 60,
            RecordedAt = DateTimeOffset.UtcNow,
            Car = car,
            Track = week.Track,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateService(db).ComputeAndCacheAsync(
            seriesId: 1,
            raceWeekIndex: 1,
            carId: car.Id,
            customerId: 2,
            evidence: PersonalBestEvidence.FromRequest(true, null),
            callerUserId: userId,
            ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(80, result.YourBestLapSeconds);
        Assert.Empty(db.CarPercentileResults);
    }

    [Fact]
    public async Task ComputeAndCacheAsync_ReturnsSeriesNameTrackNameAndDistribution()
    {
        await using var db = DbContextFactory.Create();
        var (week, car, carClass, subsession) = SeedWeekAndCar(db);
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 65);
        AddResult(db, subsession, car, carClass, custId: 2, lapSeconds: 60);
        AddResult(db, subsession, car, carClass, custId: 3, lapSeconds: 70);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateService(db)
            .ComputeAndCacheAsync(seriesId: 1, raceWeekIndex: 1, carId: 1, customerId: 1,
                evidence: OfficialEvidence, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("GT3 Cup", result.SeriesName);
        Assert.Equal("Spa", result.TrackName);
        Assert.Equal("Full", result.TrackConfigName);
        Assert.Equal(65.0, result.YourBestLapSeconds);
        Assert.Equal(LapEvidence.RaceLap, result.YourBestLapEvidence);
        Assert.Equal(60.0, result.FieldBestLapSeconds);
        Assert.Equal(65.0, result.FieldMedianLapSeconds);
        Assert.Equal(20, result.Distribution.Count);
        Assert.Contains(result.Distribution, b => b.ContainsUser);
    }

    // ── World-record overlay ──────────────────────────────────────────────────


    [Fact]
    public async Task ComputeAndCacheAsync_PopulatesWorldRecordLapAndGap()
    {
        await using var db = DbContextFactory.Create();
        var (week, car, carClass, subsession) = SeedWeekAndCar(db);
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 70);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var client = Substitute.For<IDataClient>();
        client.GetWorldRecordsAsync(
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new Aydsko.iRacingData.Common.DataResponse<(Aydsko.iRacingData.Stats.WorldRecordsHeader, Aydsko.iRacingData.Stats.WorldRecordEntry[])>
            {
                Data = (new Aydsko.iRacingData.Stats.WorldRecordsHeader(),
                    [new Aydsko.iRacingData.Stats.WorldRecordEntry { QualifyLapTime = TimeSpan.FromSeconds(66) }]),
            });
        var worldRecords = new WorldRecordService(new CachedIRacingClient(db, client));

        var result = await CreateService(db, worldRecords)
            .ComputeAndCacheAsync(seriesId: 1, raceWeekIndex: 1, carId: 1, customerId: 1,
                evidence: OfficialEvidence,
                ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(66.0, result.WorldRecordLapSeconds);
        Assert.Equal(4.0, result.WorldRecordGapSeconds!.Value, precision: 3); // 70 - 66
    }

    [Fact]
    public async Task ComputeAndCacheAsync_NoWorldRecordService_LeavesWorldRecordNull()
    {
        await using var db = DbContextFactory.Create();
        var (week, car, carClass, subsession) = SeedWeekAndCar(db);
        AddResult(db, subsession, car, carClass, custId: 1, lapSeconds: 70);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateService(db)
            .ComputeAndCacheAsync(seriesId: 1, raceWeekIndex: 1, carId: 1, customerId: 1,
                evidence: OfficialEvidence,
                ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Null(result.WorldRecordLapSeconds);
        Assert.Null(result.WorldRecordGapSeconds);
    }
}
