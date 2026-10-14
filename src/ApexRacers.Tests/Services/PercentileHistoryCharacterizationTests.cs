using System.Text.Json;
using ApexRacers.Api.Services;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ApexRacers.Tests.Services;

/// <summary>
/// Investigation #352: direct legacy service seams on fabricated PostgreSQL rows, not Live
/// authorization, protected publication, migration evidence or approved history semantics.
/// No percentile cache is seeded: every reading is produced by an actual service operation.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public class PercentileHistoryCharacterizationTests(PostgreSqlFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly PersonalBestEvidence Uploaded = PersonalBestEvidence.FromRequest(true, null);
    private static readonly PersonalBestEvidence RaceOnly = PersonalBestEvidence.OfficialRaceLapsOnly;

    [Fact]
    public async Task LegacyReal_ComputeUploadedCannotCache_RecommendationHistoryThenRaceOnlyAnalyticsRetainsUploadedRank()
    {
        await using var db = await fixture.CreateDbContextAsync(Ct);
        var graph = await SeedAsync(db);
        AddUpload(db, graph, 90, LapSessionType.Practice);
        await db.SaveChangesAsync(Ct);
        var calc = Calculator(db);
        var computed = Assert.IsType<ApexRacers.Api.Dtos.PercentileResultDto>(await calc.ComputeAndCacheAsync(
            1, 0, 1, graph.CustomerId, Uploaded, graph.UserId, Ct));
        Assert.Equal(90, computed.PercentileRank);
        Assert.Equal(LapEvidence.UploadedLap, computed.YourBestLapEvidence);
        Assert.Empty(await db.CarPercentileResults.ToListAsync(Ct));
        Assert.Null(await Subject(db).GetSubjectDriverCustIdAsync(graph.UserId, Ct));

        var recommendation = Assert.Single(await new CarRecommendationService(db).GetRecommendationsAsync(
            1, 0, graph.CustomerId, Uploaded, Ct));
        Assert.Equal(90, recommendation.PercentileRank);
        var analytics = Assert.Single(await new UserAnalyticsService(db).GetAnalyticsAsync(graph.UserId, 1, RaceOnly, Ct));
        Assert.Equal(90, analytics.LatestPercentileRank);
        Assert.Equal(110, analytics.PersonalBestLapSeconds);
        Assert.Equal(LapEvidence.RaceLap, analytics.PersonalBestLapEvidence);
        Observe("real-upload-race-only", new { computed.PercentileRank, recommendation.ExpectedPercentile,
            analytics.LatestPercentileRank, analytics.PersonalBestLapSeconds, analytics.PersonalBestLapEvidence,
            computePersisted = false, recommendationPersisted = true });
    }

    [Fact]
    public async Task LegacyReal_ChangedSessionSelectionReplacesSameWeekReadingWithoutDoubleCounting()
    {
        await using var db = await fixture.CreateDbContextAsync(Ct);
        var graph = await SeedAsync(db);
        AddUpload(db, graph, 90, LapSessionType.Practice);
        AddUpload(db, graph, 102, LapSessionType.Race);
        await db.SaveChangesAsync(Ct);
        var service = new CarRecommendationService(db);
        var practice = Assert.Single(await service.GetRecommendationsAsync(1, 0, graph.CustomerId,
            PersonalBestEvidence.FromRequest(true, [LapSessionType.Practice]), Ct));
        Assert.Equal(90, practice.PercentileRank);
        var raceUpload = Assert.Single(await service.GetRecommendationsAsync(1, 0, graph.CustomerId,
            PersonalBestEvidence.FromRequest(true, [LapSessionType.Race]), Ct));
        Assert.Equal(70, raceUpload.PercentileRank);
        Assert.Equal(70, raceUpload.ExpectedPercentile);
        var official = Assert.Single(await service.GetMyPercentilesAsync(1, 0, graph.CustomerId, RaceOnly, Ct));
        Assert.Equal(50, official.PercentileRank);
        var repeated = Assert.Single(await service.GetRecommendationsAsync(1, 0, graph.CustomerId, RaceOnly, Ct));
        Assert.Equal(50, repeated.ExpectedPercentile);
        Assert.Single(await db.CarPercentileResults.ToListAsync(Ct));
        Observe("session-replacement", new { practice = practice.PercentileRank, raceUpload = raceUpload.PercentileRank,
            official = official.PercentileRank, repeated.ExpectedPercentile, rows = 1 });
    }

    [Fact]
    public async Task Demo_BothWritersRefreshSameWeek_ThresholdCrossingReplacesEligibleContribution()
    {
        var options = await fixture.CreateOptionsAsync(Ct);
        await using var db = new AppDbContext(options, new IRacingDataScope(DataProvenance.Demo));
        DbContextFactory.InstallStoredEvidenceFixtures(db);
        var graph = await SeedAsync(db, DemoData.DriverCustId);
        db.FeatureFlags.Add(new FeatureFlag { Key = "iracing-demo", Name = "Synthetic preview", IsEnabled = true, MinimumRole = "Standard" });
        await db.SaveChangesAsync(Ct);
        Assert.Equal(DemoData.DriverCustId, await Subject(db).GetSubjectDriverCustIdAsync(graph.UserId, Ct));
        var calc = Calculator(db);
        await calc.ComputeAndCacheAsync(1, 0, 1, graph.CustomerId, RaceOnly, graph.UserId, Ct);
        var second = new Week { Id = Guid.NewGuid(), SeasonId = 10, RaceWeekIndex = 1, TrackId = 101,
            StartDate = graph.Week.StartDate.AddDays(7), EndTime = graph.Week.EndTime!.Value.AddDays(7) };
        db.Weeks.Add(second);
        await db.SaveChangesAsync(Ct);
        var s = new Subsession { Id = 2, SeasonId = 10, WeekId = second.Id, TrackId = 101, RaceWeekIndex = 1 };
        db.SubsessionResults.AddRange(Field(s, graph.CustomerId, [90, 100, 105, 115]));
        await db.SaveChangesAsync(Ct);
        await calc.ComputeAndCacheAsync(1, 1, 1, graph.CustomerId, RaceOnly, graph.UserId, Ct);
        var rec = new CarRecommendationService(db);
        var initial = Assert.Single(await rec.GetRecommendationsAsync(1, 1, graph.CustomerId, RaceOnly, Ct));
        Assert.Equal(40, initial.ExpectedPercentile); // week 0: 50; week 1: 30

        // Fabricated retained-source corrections use the fixture's ordinary model, not production
        // SQL fences. Synthetic writer refresh still enforces its actual copy lifecycle.
        var removed = await db.SubsessionResults.SingleAsync(r => r.SubsessionId == 2 && r.BestLapSeconds == 115, Ct);
        db.SubsessionResults.Remove(removed);
        await db.SaveChangesAsync(Ct);
        var down = Assert.Single(await rec.GetRecommendationsAsync(1, 1, graph.CustomerId, RaceOnly, Ct));
        Assert.Equal(50, down.ExpectedPercentile);
        var smallCalc = await calc.ComputeAndCacheAsync(1, 1, 1, graph.CustomerId, RaceOnly, graph.UserId, Ct);
        Assert.Equal(4, smallCalc!.SampleSize);
        var small = Assert.Single(await rec.GetRecommendationsAsync(1, 1, graph.CustomerId, RaceOnly, Ct));
        Assert.Equal(50, small.ExpectedPercentile);
        Assert.False(small.IsPercentilePresentable);
        Assert.Single(Assert.Single(await new UserAnalyticsService(db).GetAnalyticsAsync(graph.UserId, 1, RaceOnly, Ct)).PercentileHistory);
        db.SubsessionResults.Add(new SubsessionResult { SubsessionId = 2, CustId = 9999, CarId = 1, CarClassId = 1, BestLapSeconds = 120 });
        await db.SaveChangesAsync(Ct);
        var grown = Assert.Single(await rec.GetRecommendationsAsync(1, 1, graph.CustomerId, RaceOnly, Ct));
        Assert.Equal(40, grown.ExpectedPercentile);
        var repeat = Assert.Single(await rec.GetRecommendationsAsync(1, 1, graph.CustomerId, RaceOnly, Ct));
        Assert.Equal(40, repeat.ExpectedPercentile);
        Assert.Equal(2, await db.CarPercentileResults.CountAsync(Ct));
        Observe("threshold-and-two-writers", new { initial.ExpectedPercentile, undersizedExpected = small.ExpectedPercentile,
            grownExpected = grown.ExpectedPercentile, repeatedExpected = repeat.ExpectedPercentile, rows = 2 });
    }

    [Fact]
    public async Task Demo_ArbitraryDriverComputeDoesNotReplaceCallerHistory_ClaimIsNotSubject()
    {
        var options = await fixture.CreateOptionsAsync(Ct);
        await using var db = new AppDbContext(options, new IRacingDataScope(DataProvenance.Demo));
        DbContextFactory.InstallStoredEvidenceFixtures(db);
        var graph = await SeedAsync(db, DemoData.DriverCustId);
        db.FeatureFlags.Add(new FeatureFlag { Key = "iracing-demo", Name = "Synthetic preview", IsEnabled = true, MinimumRole = "Standard" });
        var user = await db.Users.SingleAsync(Ct);
        user.IRacingCustomerId = 777;
        AddUpload(db, graph, 90, LapSessionType.Practice);
        await db.SaveChangesAsync(Ct);
        Assert.Empty(await db.UploadedLaps.ToListAsync(Ct)); // Demo cannot read quarantined legacy uploads.
        Assert.Equal(DemoData.DriverCustId, await Subject(db).GetSubjectDriverCustIdAsync(graph.UserId, Ct));
        var demoComputed = await Calculator(db).ComputeAndCacheAsync(1, 0, 1, DemoData.DriverCustId, Uploaded, graph.UserId, Ct);
        Assert.Equal(LapEvidence.RaceLap, demoComputed!.YourBestLapEvidence);
        var own = await db.CarPercentileResults.AsNoTracking().SingleAsync(Ct);
        var other = await Calculator(db).ComputeAndCacheAsync(1, 0, 1, 1000, Uploaded, graph.UserId, Ct);
        Assert.NotNull(other);
        var retained = await db.CarPercentileResults.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(own.PercentileRank, retained.PercentileRank);
        Assert.Equal(own.ComputedAt, retained.ComputedAt);
        Assert.Equal(1, await db.CarPercentileResults.CountAsync(Ct));
        var recommendation = Assert.Single(await new CarRecommendationService(db).GetRecommendationsAsync(
            1, 0, DemoData.DriverCustId, RaceOnly, Ct));
        Assert.Equal(50, recommendation.ExpectedPercentile);
        Assert.Equal(own.ComputedAt, (await db.CarPercentileResults.AsNoTracking().SingleAsync(Ct)).ComputedAt);
        var analytics = Assert.Single(await new UserAnalyticsService(db).GetAnalyticsAsync(graph.UserId, 1, RaceOnly, Ct));
        Assert.Null(analytics.PersonalBestLapSeconds);
        Assert.Equal(0, analytics.TotalWeeks);
        Assert.Equal(50, analytics.LatestPercentileRank);
        Observe("subject-ownership", new { analytics.PersonalBestLapSeconds, analytics.TotalWeeks, recommendation.ExpectedPercentile,
            demoUploadedVisible = false, demoComputed.YourBestLapEvidence, recommendationPersisted = false,
            claimedCustomerId = 777, subjectCustomerId = DemoData.DriverCustId,
            own.PercentileRank, otherRank = other!.PercentileRank, retained = true });
    }

    [Fact]
    public async Task LegacyReal_UploadSelectionUsesTrackIdAndHalfOpenWeek_ExcludedFastestKeepsItsDate()
    {
        await using var db = await fixture.CreateDbContextAsync(Ct);
        var graph = await SeedAsync(db);
        db.Tracks.Add(new Track { Id = 102, Name = "Synthetic shared venue", ConfigName = "Other layout" });
        AddUpload(db, graph, 1, LapSessionType.Practice, trackId: 102);
        AddUpload(db, graph, 2, LapSessionType.Practice, at: graph.Week.EndTime!.Value);
        AddUpload(db, graph, 3, LapSessionType.Practice, at: graph.Week.EndTime.Value.AddHours(1));
        AddUpload(db, graph, 90, LapSessionType.Practice);
        await db.SaveChangesAsync(Ct);
        var computed = await Calculator(db).ComputeAndCacheAsync(1, 0, 1, graph.CustomerId, Uploaded, graph.UserId, Ct);
        Assert.Equal(90, computed!.YourBestLapSeconds);
        Assert.Equal(2, computed.UploadedBestOutsideWeek!.LapSeconds);
        Assert.Equal(graph.Week.EndTime.Value, computed.UploadedBestOutsideWeek.RecordedAt);
        var rec = Assert.Single(await new CarRecommendationService(db).GetRecommendationsAsync(1, 0, graph.CustomerId, Uploaded, Ct));
        Assert.Equal(90, rec.BestLapSeconds);
        var analytics = Assert.Single(await new UserAnalyticsService(db).GetAnalyticsAsync(graph.UserId, 1, Uploaded, Ct));
        Assert.Equal(90, analytics.PersonalBestLapSeconds);
        Observe("track-week-scope", new { computed.YourBestLapSeconds, excluded = computed.UploadedBestOutsideWeek,
            analytics.PersonalBestLapSeconds });
    }

    private static SubjectDriverContext Subject(AppDbContext db) => new(db, new FeatureFlagEligibility(db));
    private static PercentileCalculationService Calculator(AppDbContext db) => new(db, Subject(db));
    private sealed record Graph(Guid UserId, long CustomerId, Week Week);

    private static async Task<Graph> SeedAsync(AppDbContext db, long customerId = 42)
    {
        var userId = Guid.NewGuid();
        var start = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-14);
        db.Users.Add(new ApplicationUser { DisplayName = "Synthetic User", Id = userId, UserName = "synthetic@example.invalid", NormalizedUserName = "SYNTHETIC@EXAMPLE.INVALID",
            Email = "synthetic@example.invalid", NormalizedEmail = "SYNTHETIC@EXAMPLE.INVALID", EmailConfirmed = true, IRacingCustomerId = customerId });
        db.Series.Add(new Series { Id = 1, Name = "Synthetic Series" });
        db.Seasons.Add(new Season { Id = 10, SeriesId = 1, Year = 2026, Quarter = 4, Active = true });
        db.Cars.Add(new Car { Id = 1, Name = "Synthetic Car", NameAbbreviated = "S" });
        db.CarClasses.Add(new CarClass { Id = 1, Name = "Synthetic Class", ShortName = "S" });
        db.Tracks.Add(new Track { Id = 101, Name = "Synthetic shared venue", ConfigName = "First layout" });
        var week = new Week { Id = Guid.NewGuid(), SeasonId = 10, RaceWeekIndex = 0, TrackId = 101, StartDate = start,
            EndTime = new DateTimeOffset(start.AddDays(7).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) };
        db.Weeks.Add(week);
        var s = new Subsession { Id = 1, SeasonId = 10, WeekId = week.Id, TrackId = 101, RaceWeekIndex = 0 };
        db.SubsessionResults.AddRange(Field(s, customerId, [100, 105, 115, 120]));
        await db.SaveChangesAsync(Ct);
        return new(userId, customerId, week);
    }

    private static IEnumerable<SubsessionResult> Field(Subsession s, long customerId, double[] others) =>
        others.Select((lap, i) => new SubsessionResult { Subsession = s, CustId = 1000 + i, CarId = 1, CarClassId = 1, BestLapSeconds = lap })
            .Append(new SubsessionResult { Subsession = s, CustId = customerId, CarId = 1, CarClassId = 1, BestLapSeconds = 110 });

    private static void AddUpload(AppDbContext db, Graph graph, double lap, LapSessionType type,
        DateTimeOffset? at = null, int trackId = 101) => db.UploadedLaps.Add(new UploadedLap { UserId = graph.UserId,
            CarId = 1, TrackId = trackId, LapTimeSeconds = lap, SessionType = type,
            RecordedAt = at ?? new DateTimeOffset(graph.Week.StartDate.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero) });

    private static void Observe(string name, object outcome)
    {
        // MTP starts inside bin/<configuration>/net10.0; keep optional observations in the
        // repository's ignored evidence directory instead of treating copied binaries as a root.
        var root = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (!File.Exists(Path.Combine(root.FullName, "ApexRacers.sln")) && root.Parent is { } parent)
            root = parent;
        var directory = Path.Combine(root.FullName, "TestResults", "352-history");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(new
        { scenario = name, boundary = "fabricated PostgreSQL direct legacy service seam; no Live authorization", outcome },
            new JsonSerializerOptions { WriteIndented = true }));
    }
}
