using ApexRacers.Api.Dtos;
using ApexRacers.Api.Services;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ApexRacers.Tests.Services;

/// <summary>Current analytics is reconstructed from retained, classified PostgreSQL evidence.
/// The fixture adapter supplies synthetic retained sources; it establishes neither acquisition nor Live authority.</summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class UserAnalyticsServiceTests(PostgreSqlFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly PersonalBestEvidence RaceOnly = PersonalBestEvidence.OfficialRaceLapsOnly;

    [Fact]
    public async Task NoRetainedEvidenceOrUnknownUserReturnsEmptyHistory()
    {
        await using var db = await CreateAsync();
        var user = AddUser(db);
        await db.SaveChangesAsync(Ct);
        Assert.Empty(await ReadAsync(db, user));
        Assert.Empty(await ReadAsync(db, Guid.NewGuid()));
        Assert.Empty(await db.ClassifiedCarPercentileResults.ToListAsync(Ct));
    }

    [Fact]
    public async Task SingleReadingCarriesTheSameSubjectPersonalBestRankFieldAndSelection()
    {
        await using var db = await CreateAsync();
        var user = AddUser(db, 777);
        var (week, car) = Graph(db, 1, 101);
        AddField(db, week, car, 65, [60, 70, 80, 90]);
        await db.SaveChangesAsync(Ct);
        var dto = Assert.Single(await ReadAsync(db, user));
        var reading = Assert.Single(dto.PercentileHistory);
        Assert.Equal(car.Id, dto.CarId); Assert.Equal(1, dto.SeriesId);
        Assert.Equal(DemoData.DriverCustId, dto.CustomerId);
        Assert.Equal(70, dto.LatestPercentileRank); Assert.Equal(70, dto.BestPercentileRank);
        Assert.Equal(65, reading.PersonalBestLapSeconds);
        Assert.Equal(LapEvidence.RaceLap, reading.PersonalBestLapEvidence);
        Assert.Equal(70, reading.PercentileRank); Assert.Equal(40, reading.TopSharePercent);
        Assert.Equal(5, reading.SampleSize); Assert.Equal(70, reading.MedianLapSeconds);
        Assert.Equal(week.Id, reading.WeekId); Assert.Equal(week.SeasonId, reading.SeasonId);
        Assert.Equal(week.StartDate, reading.WeekStartDate); Assert.Equal(week.TrackId, reading.TrackId);
        Assert.Equal(RaceOnly.Selection.Key, reading.SelectionKey);
        Assert.Equal(RaceOnly.Selection.PolicyVersion, reading.SelectionPolicyVersion);
        Assert.Equal(reading.SelectionKey, dto.SelectionKey);
        Assert.Equal(1, dto.TotalWeeks);
        Assert.Empty(await db.CarPercentileResults.ToListAsync(Ct));
    }

    [Fact]
    public async Task SelectionHistoriesStaySeparateAndQuarantinedUploadsNeverOverlayDemo()
    {
        await using var db = await CreateAsync();
        var user = AddUser(db);
        var (week, car) = Graph(db, 1, 101);
        AddField(db, week, car, 80, [60, 70, 90, 95]);
        db.UploadedLaps.Add(new() { UserId = user, CarId = car.Id, TrackId = week.TrackId,
            LapTimeSeconds = 1, SessionType = LapSessionType.Race, RecordedAt = DateTimeOffset.UtcNow.AddDays(-2) });
        await db.SaveChangesAsync(Ct);
        var selections = new[] { RaceOnly, PersonalBestEvidence.FromRequest(true, null),
            PersonalBestEvidence.FromRequest(true, [LapSessionType.Practice]) };
        foreach (var selection in selections)
        {
            var reading = Assert.Single(Assert.Single(await ReadAsync(db, user, selection)).PercentileHistory);
            Assert.Equal(80, reading.PersonalBestLapSeconds); Assert.Equal(50, reading.PercentileRank);
            Assert.Equal(LapEvidence.RaceLap, reading.PersonalBestLapEvidence); Assert.Null(reading.UploadedSessionType);
            Assert.Equal(selection.Selection.Key, reading.SelectionKey);
        }
        Assert.Equal(3, await db.ClassifiedCarPercentileResults.CountAsync(Ct));
        Assert.Equal(80, Assert.Single(await ReadAsync(db, user)).PersonalBestLapSeconds);
        Assert.Equal(3, await db.ClassifiedCarPercentileResults.CountAsync(Ct));
    }

    [Fact]
    public async Task WeeklyPersonalBestAndMedianStayPairedWhenSummaryFastestAndBestRankComeFromOtherWeek()
    {
        await using var db = await CreateAsync();
        var user = AddUser(db);
        var (first, car) = Graph(db, 1, 101, days: -14);
        var (last, _) = Graph(db, 1, 102, index: 1, days: -7);
        AddField(db, first, car, 60, [65, 70, 75, 80]);
        AddField(db, last, car, 110, [90, 100, 105, 120]);
        await db.SaveChangesAsync(Ct);
        var dto = Assert.Single(await ReadAsync(db, user));
        Assert.Equal(new[] { first.Id, last.Id }, dto.PercentileHistory.Select(r => r.WeekId));
        Assert.Equal(2, dto.TotalWeeks);
        Assert.Equal(90, dto.BestPercentileRank); Assert.Equal(30, dto.LatestPercentileRank);
        Assert.Equal(60, dto.PersonalBestLapSeconds); // Explicit aggregate across Weeks remains distinct.
        Assert.Equal(70, dto.MedianLapSeconds); // Compatibility summary: best-rank Week.
        var latest = dto.PercentileHistory[^1];
        Assert.Equal(110, latest.PersonalBestLapSeconds); Assert.Equal(105, latest.MedianLapSeconds);
        Assert.Equal(dto.LatestPercentileRank, latest.PercentileRank);
        Assert.Equal(LapEvidence.RaceLap, latest.PersonalBestLapEvidence);
    }

    [Fact]
    public async Task SeriesFilterUsesOnlyItsCurrentSourceCatalog()
    {
        await using var db = await CreateAsync();
        var user = AddUser(db);
        var (first, car1) = Graph(db, 1, 101);
        var (other, car2) = Graph(db, 2, 201);
        AddField(db, first, car1, 60, [65, 70, 75, 80]);
        AddField(db, other, car2, 75, [65, 70, 80, 85]);
        await db.SaveChangesAsync(Ct);
        Assert.Equal(2, (await ReadAsync(db, user)).Count);
        var dto = Assert.Single(await new UserAnalyticsService(db).GetAnalyticsAsync(user, 1, RaceOnly, Ct));
        Assert.Equal(1, dto.SeriesId); Assert.Equal(car1.Id, dto.CarId);
    }

    [Fact]
    public async Task DemoWithoutAClaimStillResolvesItsOwnSyntheticSubject()
    {
        await using var db = await CreateAsync();
        var user = AddUser(db, null);
        var (week, car) = Graph(db, 1, 101);
        AddField(db, week, car, 65, [60, 70, 80, 90]);
        await db.SaveChangesAsync(Ct);
        var dto = Assert.Single(await ReadAsync(db, user));
        Assert.Equal(DemoData.DriverCustId, dto.CustomerId);
        Assert.Equal(65, dto.PersonalBestLapSeconds);
        Assert.Equal(70, dto.LatestPercentileRank);
    }

    [Fact]
    public async Task MultipleCarsAreSortedByTheirOwnBestCurrentReading()
    {
        await using var db = await CreateAsync();
        var user = AddUser(db);
        var (week, car) = Graph(db, 1, 101);
        var other = new Car { Id = 999, Name = "Second synthetic Car", NameAbbreviated = "SSC" };
        db.Cars.Add(other);
        AddField(db, week, car, 70, [60, 65, 80, 90]);
        AddField(db, week, other, 50, [60, 70, 80, 90]);
        await db.SaveChangesAsync(Ct);
        var result = await ReadAsync(db, user);
        Assert.Equal(new[] { other.Id, car.Id }, result.Select(r => r.CarId));
        Assert.Equal(90, result[0].BestPercentileRank); Assert.Equal(50, result[1].BestPercentileRank);
    }

    [Fact]
    public async Task LosingPresentableFieldOrPersonalBestRemovesPreviousHeadlineContribution()
    {
        await using var db = await CreateAsync();
        var user = AddUser(db);
        var (week, car) = Graph(db, 1, 101);
        var race = AddField(db, week, car, 65, [60, 70, 80, 90]);
        await db.SaveChangesAsync(Ct);
        Assert.Single(await ReadAsync(db, user));
        await RewriteAsync(db, race.Id, rows => db.SubsessionResults.Remove(rows.Single(r => r.BestLapSeconds == 90)));
        Assert.Empty(await ReadAsync(db, user));
        Assert.Empty(await db.ClassifiedCarPercentileResults.ToListAsync(Ct));
        await RewriteAsync(db, race.Id, rows => db.SubsessionResults.AddRange(
            new SubsessionResult { SubsessionId = race.Id, CustId = 9998, CarId = car.Id, CarClassId = 1, BestLapSeconds = 95 },
            new SubsessionResult { SubsessionId = race.Id, CustId = 9999, CarId = car.Id, CarClassId = 1, BestLapSeconds = 100 }));
        Assert.Single(await ReadAsync(db, user));
        await RewriteAsync(db, race.Id, rows => db.SubsessionResults.Remove(rows.Single(r => r.CustId == DemoData.DriverCustId)));
        Assert.Empty(await ReadAsync(db, user));
        Assert.Empty(await db.ClassifiedCarPercentileResults.ToListAsync(Ct));
    }

    [Fact]
    public async Task CompleteSourceLossCannotResurrectStoredHistory()
    {
        await using var db = await CreateAsync();
        var user = AddUser(db);
        var (week, car) = Graph(db, 1, 101);
        var race = AddField(db, week, car, 65, [60, 70, 80, 90]);
        await db.SaveChangesAsync(Ct);
        Assert.Single(await ReadAsync(db, user));
        var marker = Assert.Single(await db.ClassifiedCarPercentileResults.ToListAsync(Ct)).EvidenceCopyId;
        var writer = await SyntheticEvidenceWriter.OpenAsync(db, Ct);
        db.SubsessionResults.RemoveRange(await db.SubsessionResults.Where(r => r.SubsessionId == race.Id).ToListAsync(Ct));
        db.Subsessions.Remove(race);
        await writer.SaveChangesAsync(Ct);
        Assert.Empty(await ReadAsync(db, user));
        Assert.Empty(await db.ClassifiedCarPercentileResults.ToListAsync(Ct));
        Assert.NotNull((await db.EvidenceCopyMarkers.SingleAsync(m => m.Id == marker, Ct)).UnavailableAt);
    }

    [Theory]
    [InlineData(DataProvenance.Real, true)]
    [InlineData(DataProvenance.Demo, false)]
    public async Task RealClaimOrDisabledDemoCannotReadEvenAConflictingLegacyCache(DataProvenance provenance, bool enabled)
    {
        await using var db = await CreateAsync(provenance, enabled);
        var user = AddUser(db, 42);
        var (week, car) = Graph(db, 1, 101);
        AddField(db, week, car, 65, [60, 70, 80, 90]);
        db.CarPercentileResults.Add(new() { UserId = user, CarId = car.Id, SeriesId = 1, WeekId = week.Id,
            PercentileRank = 99, TopSharePercent = 1, SampleSize = 100, ComputedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(Ct);
        Assert.Empty(await ReadAsync(db, user));
        Assert.Empty(await db.ClassifiedCarPercentileResults.ToListAsync(Ct));
        Assert.Equal(99, Assert.Single(await db.CarPercentileResults.ToListAsync(Ct)).PercentileRank);
    }

    [Fact]
    public async Task EqualStartDatesAcrossSeasonsRemainDistinctWithStableChronologicalPairing()
    {
        await using var db = await CreateAsync();
        var user = AddUser(db);
        var (week, car) = Graph(db, 1, 101);
        db.Seasons.Add(new() { Id = 11, SeriesId = 1, Year = 2026, Quarter = 3 });
        var other = new Week { Id = Guid.NewGuid(), SeasonId = 11, RaceWeekIndex = 0, TrackId = 101, StartDate = week.StartDate };
        db.Weeks.Add(other);
        AddField(db, week, car, 60, [65, 70, 75, 80]);
        AddField(db, other, car, 75, [65, 70, 80, 85]);
        await db.SaveChangesAsync(Ct);
        var dto = Assert.Single(await ReadAsync(db, user));
        Assert.Equal(new[] { week.Id, other.Id }.Order(), dto.PercentileHistory.Select(r => r.WeekId));
        Assert.Equal(2, dto.TotalWeeks);
        Assert.All(dto.PercentileHistory, r => Assert.Equal(week.StartDate, r.WeekStartDate));
        Assert.Equal(dto.PercentileHistory[^1].PercentileRank, dto.LatestPercentileRank);
        Assert.Equal(dto.PercentileHistory.Select(r => r.WeekId), Assert.Single(await ReadAsync(db, user)).PercentileHistory.Select(r => r.WeekId));
    }

    private static Task<List<CarAnalyticsDto>> ReadAsync(AppDbContext db, Guid user, PersonalBestEvidence? evidence = null) =>
        new UserAnalyticsService(db).GetAnalyticsAsync(user, null, evidence ?? RaceOnly, Ct);

    private async Task<AppDbContext> CreateAsync(DataProvenance provenance = DataProvenance.Demo, bool enabled = true)
    {
        var options = await postgres.CreateOptionsAsync(Ct);
        var db = new AppDbContext(options, new IRacingDataScope(provenance));
        DbContextFactory.InstallStoredEvidenceFixtures(db);
        db.FeatureFlags.Add(new() { Key = "iracing-demo", Name = "Synthetic preview", IsEnabled = enabled, MinimumRole = "Standard" });
        await db.SaveChangesAsync(Ct);
        return db;
    }

    private static Guid AddUser(AppDbContext db, long? claim = 777)
    {
        var id = Guid.NewGuid();
        db.Users.Add(new() { Id = id, DisplayName = "Synthetic User", IRacingCustomerId = claim });
        return id;
    }

    private static (Week Week, Car Car) Graph(AppDbContext db, int series, int track, int index = 0, int days = -7)
    {
        if (!db.Series.Local.Any(s => s.Id == series))
        {
            db.Series.Add(new() { Id = series, Name = $"Synthetic Series {series}" });
            db.Seasons.Add(new() { Id = series * 10, SeriesId = series, Year = 2026, Quarter = 4, Active = true });
        }
        if (!db.Tracks.Local.Any(t => t.Id == track)) db.Tracks.Add(new() { Id = track, Name = "Shared venue", ConfigName = $"Layout {track}" });
        var week = new Week { Id = Guid.NewGuid(), SeasonId = series * 10, RaceWeekIndex = index, TrackId = track,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(days) };
        db.Weeks.Add(week);
        var car = db.Cars.Local.SingleOrDefault(c => c.Id == series * 100 + 1);
        if (car is null)
        {
            car = new() { Id = series * 100 + 1, Name = $"Synthetic Car {series}", NameAbbreviated = "SC" };
            db.Cars.Add(car);
        }
        if (!db.CarClasses.Local.Any()) db.CarClasses.Add(new() { Id = 1, Name = "Synthetic Class", ShortName = "SC" });
        return (week, car);
    }

    private static Subsession AddField(AppDbContext db, Week week, Car car, double best, double[] others)
    {
        var race = new Subsession { Id = db.Subsessions.Local.Count + 1, SeasonId = week.SeasonId,
            RaceWeekIndex = week.RaceWeekIndex, WeekId = week.Id, TrackId = week.TrackId };
        db.Subsessions.Add(race);
        db.SubsessionResults.AddRange(others.Select((lap, i) => new SubsessionResult { SubsessionId = race.Id,
            CustId = 1000 + i, CarId = car.Id, CarClassId = 1, BestLapSeconds = lap }).Append(new()
            { SubsessionId = race.Id, CustId = DemoData.DriverCustId, CarId = car.Id, CarClassId = 1, BestLapSeconds = best }));
        return race;
    }

    private static async Task RewriteAsync(AppDbContext db, int race, Action<List<SubsessionResult>> mutate)
    {
        var writer = await SyntheticEvidenceWriter.OpenAsync(db, Ct);
        var header = await db.Subsessions.SingleAsync(r => r.Id == race, Ct);
        var rows = await db.SubsessionResults.Where(r => r.SubsessionId == race).ToListAsync(Ct);
        db.Entry(header).State = EntityState.Modified;
        foreach (var row in rows) db.Entry(row).State = EntityState.Modified;
        mutate(rows);
        await writer.SaveChangesAsync(Ct);
    }
}
