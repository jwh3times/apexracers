using System.Data.Common;
using ApexRacers.Api.Services;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace ApexRacers.Tests.Services;

/// <summary>Successive PostgreSQL service calls for the accepted refresh contract. The original
/// #352 observation artifacts remain historical; these current tests establish neither Live nor HTTP authority.</summary>
[Collection(PostgreSqlCollection.Name)]
public class PercentileHistoryCharacterizationTests(PostgreSqlFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly PersonalBestEvidence Uploaded = PersonalBestEvidence.FromRequest(true, null);
    private static readonly PersonalBestEvidence RaceOnly = PersonalBestEvidence.OfficialRaceLapsOnly;

    [Theory]
    [InlineData("no-caller")]
    [InlineData("unknown-user")]
    [InlineData("other-driver")]
    [InlineData("disabled-demo")]
    public async Task IneligibleHistoryRetainsCallerSpecificFallbackWithoutClassifiedWrites(string refusal)
    {
        await using var db = await DemoAsync();
        var graph = await SeedAsync(db, DemoData.DriverCustId);
        if (refusal == "disabled-demo")
        {
            (await db.FeatureFlags.SingleAsync(Ct)).IsEnabled = false;
            await db.SaveChangesAsync(Ct);
        }
        Guid? caller = refusal switch
        {
            "no-caller" => null,
            "unknown-user" => Guid.NewGuid(),
            _ => graph.UserId
        };
        var customerId = refusal == "other-driver" ? 1000 : graph.CustomerId;
        var result = await Calculator(db).ComputeAndCacheAsync(1, 0, 1, customerId, Uploaded, caller, Ct);
        Assert.Equal(refusal == "other-driver" ? 90 : 50, result!.PercentileRank);
        Assert.Equal(LapEvidence.RaceLap, result.YourBestLapEvidence);
        var recommendation = Assert.Single(await new CarRecommendationService(db).GetRecommendationsAsync(
            1, 0, customerId, Uploaded, caller, Ct));
        Assert.Null(recommendation.ExpectedPercentile);
        Assert.Equal(result.PercentileRank, recommendation.PercentileRank);
        if (refusal is "unknown-user" or "disabled-demo")
            Assert.Empty(await new UserAnalyticsService(db).GetAnalyticsAsync(caller!.Value, 1, Uploaded, Ct));
        Assert.Empty(await db.ClassifiedCarPercentileResults.ToListAsync(Ct));
    }

    [Fact]
    public async Task SelectedUnavailableSubjectScopePropagatesBeforeHistoryOrTransientFallback()
    {
        await using var db = await DemoAsync();
        var graph = await SeedAsync(db, DemoData.DriverCustId);
        var subjects = new SubjectDriverContext(db, new FeatureFlagEligibility(db),
            new IRacingDataScope(DataProvenance.Unknown));
        await Assert.ThrowsAsync<IRacingNotConfiguredException>(() =>
            new UserAnalyticsService(db, subjects).GetAnalyticsAsync(graph.UserId, 1, RaceOnly, Ct));
        await Assert.ThrowsAsync<IRacingNotConfiguredException>(() =>
            new PercentileCalculationService(db, subjects).ComputeAndCacheAsync(
                1, 0, 1, graph.CustomerId, RaceOnly, graph.UserId, Ct));
        await Assert.ThrowsAsync<IRacingNotConfiguredException>(() =>
            new CarRecommendationService(db, subjects).GetRecommendationsAsync(
                1, 0, graph.CustomerId, RaceOnly, graph.UserId, Ct));
        Assert.Empty(await db.ClassifiedCarPercentileResults.ToListAsync(Ct));
    }

    [Fact]
    public async Task RealNamespaceShortCircuitsUnavailableSubjectResolver()
    {
        await using var db = await fixture.CreateDbContextAsync(Ct);
        var graph = await SeedAsync(db);
        var subjects = new SubjectDriverContext(db, new FeatureFlagEligibility(db),
            new IRacingDataScope(DataProvenance.Unknown));
        Assert.Empty(await new UserAnalyticsService(db, subjects).GetAnalyticsAsync(graph.UserId, 1, RaceOnly, Ct));
        Assert.NotNull(await new PercentileCalculationService(db, subjects).ComputeAndCacheAsync(
            1, 0, 1, graph.CustomerId, RaceOnly, graph.UserId, Ct));
        Assert.Single(await new CarRecommendationService(db, subjects).GetRecommendationsAsync(
            1, 0, graph.CustomerId, RaceOnly, graph.UserId, Ct));
        Assert.Empty(await db.ClassifiedCarPercentileResults.ToListAsync(Ct));
    }

    [Fact]
    public async Task SelectedDemoScopeOverridesDisabledFlagAndInjectedRefreshClockIsPreserved()
    {
        await using var db = await DemoAsync();
        var graph = await SeedAsync(db, DemoData.DriverCustId);
        (await db.FeatureFlags.SingleAsync(Ct)).IsEnabled = false;
        (await db.Users.SingleAsync(Ct)).IRacingCustomerId = null;
        await db.SaveChangesAsync(Ct);
        var subjects = new SubjectDriverContext(db, new FeatureFlagEligibility(db),
            new IRacingDataScope(DataProvenance.Demo));
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60);
        var refresh = new PercentileRefresh(db, new FrozenClock(now), new UnavailableDriverEnforcementJournal());
        var result = await new PercentileCalculationService(db, subjects, refresh: refresh).ComputeAndCacheAsync(
            1, 0, 1, graph.CustomerId, RaceOnly, graph.UserId, Ct);
        Assert.Equal(now, result!.ComputedAt);
        var recommendation = Assert.Single(await new CarRecommendationService(db, subjects, refresh)
            .GetRecommendationsAsync(1, 0, graph.CustomerId, RaceOnly, graph.UserId, Ct));
        Assert.Equal(result.PercentileRank, recommendation.ExpectedPercentile);
        Assert.Single(await new UserAnalyticsService(db, subjects, refresh).GetAnalyticsAsync(graph.UserId, 1, RaceOnly, Ct));
        Assert.Equal(now, Assert.Single(await db.ClassifiedCarPercentileResults.AsNoTracking().ToListAsync(Ct)).ComputedAt);
    }

    [Fact]
    public async Task CallerCancellationPropagatesAcrossAllDemoReaders()
    {
        await using var db = await DemoAsync();
        var graph = await SeedAsync(db, DemoData.DriverCustId);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new UserAnalyticsService(db)
            .GetAnalyticsAsync(graph.UserId, 1, RaceOnly, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Calculator(db).ComputeAndCacheAsync(
            1, 0, 1, graph.CustomerId, RaceOnly, graph.UserId, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CarRecommendationService(db)
            .GetRecommendationsAsync(1, 0, graph.CustomerId, RaceOnly, graph.UserId, cancellation.Token));
        Assert.Empty(await db.ClassifiedCarPercentileResults.ToListAsync(Ct));
    }

    [Fact]
    public async Task AdmittedEmptyHistoryDoesNotRunTransientFieldQueries()
    {
        var options = await fixture.CreateOptionsAsync(Ct, new RejectTransientFieldRead());
        await using var db = new AppDbContext(options, new IRacingDataScope(DataProvenance.Demo));
        DbContextFactory.InstallStoredEvidenceFixtures(db);
        db.FeatureFlags.Add(new() { Key = "iracing-demo", Name = "Synthetic preview", IsEnabled = true, MinimumRole = "Standard" });
        var graph = await SeedAsync(db, DemoData.DriverCustId);
        var writer = await SyntheticEvidenceWriter.OpenAsync(db, Ct);
        db.SubsessionResults.RemoveRange(await db.SubsessionResults.ToListAsync(Ct));
        await writer.SaveChangesAsync(Ct);
        // Prove this guard sees the production transient query, even over an empty Field.
        await Assert.ThrowsAsync<InvalidOperationException>(() => Calculator(db).ComputeAndCacheAsync(
            1, 0, 1, graph.CustomerId, RaceOnly, ct: Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new CarRecommendationService(db)
            .GetRecommendationsAsync(1, 0, graph.CustomerId, RaceOnly, Ct));
        Assert.Null(await Calculator(db).ComputeAndCacheAsync(1, 0, 1, graph.CustomerId, RaceOnly, graph.UserId, Ct));
        Assert.Empty(await new CarRecommendationService(db).GetRecommendationsAsync(
            1, 0, graph.CustomerId, RaceOnly, graph.UserId, Ct));
        Assert.Empty(await new UserAnalyticsService(db).GetAnalyticsAsync(graph.UserId, 1, RaceOnly, Ct));
    }

    [Fact]
    public async Task AdmittedHistoryRefreshFailurePropagatesInsteadOfReadingTransientField()
    {
        await using var db = await DemoAsync();
        var graph = await SeedAsync(db, DemoData.DriverCustId);
        await db.EvidenceCopyMarkers.Where(m => m.Kind == EvidenceCopyKind.OfficialField)
            .ExecuteUpdateAsync(u => u.SetProperty(m => m.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)), Ct);
        // Legacy query availability alone is not sufficient for classified refresh admission.
        Assert.Equal(5, await db.SubsessionResults.CountAsync(Ct));
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => Calculator(db).ComputeAndCacheAsync(
            1, 0, 1, graph.CustomerId, RaceOnly, graph.UserId, Ct));
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => new CarRecommendationService(db)
            .GetRecommendationsAsync(1, 0, graph.CustomerId, RaceOnly, graph.UserId, Ct));
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => new UserAnalyticsService(db)
            .GetAnalyticsAsync(graph.UserId, 1, RaceOnly, Ct));
        Assert.Empty(await db.ClassifiedCarPercentileResults.ToListAsync(Ct));
    }

    private sealed class RejectTransientFieldRead : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("GROUP BY", StringComparison.Ordinal)
                && command.CommandText.Contains("\"RaceEvidenceResults\"", StringComparison.Ordinal))
                throw new InvalidOperationException("An admitted empty history must not enter transient Field queries.");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FrozenClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public async Task RealClaimAndQuarantineCannotCreateOrMixPersonalHistory()
    {
        await using var db = await fixture.CreateDbContextAsync(Ct);
        var graph = await SeedAsync(db);
        AddUpload(db, graph, 90, LapSessionType.Practice);
        await db.SaveChangesAsync(Ct);
        var computed = await Calculator(db).ComputeAndCacheAsync(1, 0, 1, graph.CustomerId, Uploaded, graph.UserId, Ct);
        Assert.Equal(50, computed!.PercentileRank);
        Assert.Equal(LapEvidence.RaceLap, computed.YourBestLapEvidence);
        var recommendation = Assert.Single(await new CarRecommendationService(db).GetRecommendationsAsync(
            1, 0, graph.CustomerId, Uploaded, graph.UserId, Ct));
        Assert.Equal(50, recommendation.PercentileRank);
        Assert.Null(recommendation.ExpectedPercentile);
        Assert.Empty(await db.CarPercentileResults.ToListAsync(Ct));
        Assert.Empty(await db.ClassifiedCarPercentileResults.ToListAsync(Ct));
        Assert.Empty(await new UserAnalyticsService(db).GetAnalyticsAsync(graph.UserId, 1, RaceOnly, Ct));
    }

    [Fact]
    public async Task BothApiWritersShareClassifiedRowsAndIndependentSelectionsWithoutClaimOwnership()
    {
        await using var db = await DemoAsync();
        var graph = await SeedAsync(db, DemoData.DriverCustId);
        (await db.Users.SingleAsync(Ct)).IRacingCustomerId = 777;
        AddUpload(db, graph, 1, LapSessionType.Practice);
        await db.SaveChangesAsync(Ct);
        var computed = await Calculator(db).ComputeAndCacheAsync(1, 0, 1, graph.CustomerId, RaceOnly, graph.UserId, Ct);
        Assert.Equal(50, computed!.PercentileRank);
        Assert.Single(await db.ClassifiedCarPercentileResults.ToListAsync(Ct));
        var rec = new CarRecommendationService(db);
        var repeated = Assert.Single(await rec.GetRecommendationsAsync(1, 0, graph.CustomerId, RaceOnly, graph.UserId, Ct));
        Assert.Equal(50, repeated.ExpectedPercentile);
        var raceRow = Assert.Single(await db.ClassifiedCarPercentileResults.AsNoTracking().ToListAsync(Ct));
        Assert.Equal(RaceOnly.Selection.Key, raceRow.SelectionKey);
        Assert.Equal(graph.UserId, raceRow.UserId);
        Assert.Equal(DemoData.DriverCustId, raceRow.CustomerId);
        Assert.Equal(110, raceRow.PersonalBestLapSeconds);
        await rec.GetRecommendationsAsync(1, 0, graph.CustomerId, Uploaded, graph.UserId, Ct);
        Assert.Equal(2, await db.ClassifiedCarPercentileResults.CountAsync(Ct));
        var stable = await db.ClassifiedCarPercentileResults.AsNoTracking().OrderBy(r => r.SelectionKey)
            .Select(r => new { r.Id, r.ComputedAt, r.EvidenceCopyId }).ToListAsync(Ct);
        var other = await Calculator(db).ComputeAndCacheAsync(1, 0, 1, 1000, Uploaded, graph.UserId, Ct);
        Assert.Equal(90, other!.PercentileRank);
        await rec.GetRecommendationsAsync(1, 0, 1000, Uploaded, graph.UserId, Ct);
        Assert.Equal(stable, await db.ClassifiedCarPercentileResults.AsNoTracking().OrderBy(r => r.SelectionKey)
            .Select(r => new { r.Id, r.ComputedAt, r.EvidenceCopyId }).ToListAsync(Ct));
        Assert.Empty(await db.CarPercentileResults.ToListAsync(Ct));
        Assert.Empty(await db.UploadedLaps.ToListAsync(Ct));
    }

    [Fact]
    public async Task BothWritersReplaceThresholdContributionAndRemoveWeekWithNoPersonalBest()
    {
        await using var db = await DemoAsync();
        var graph = await SeedAsync(db, DemoData.DriverCustId);
        var second = new Week { Id = Guid.NewGuid(), SeasonId = 10, RaceWeekIndex = 1, TrackId = 101,
            StartDate = graph.Week.StartDate.AddDays(7), EndTime = graph.Week.EndTime!.Value.AddDays(7) };
        db.Weeks.Add(second);
        var s = new Subsession { Id = 2, SeasonId = 10, WeekId = second.Id, TrackId = 101, RaceWeekIndex = 1 };
        db.SubsessionResults.AddRange(Field(s, graph.CustomerId, [90, 100, 105, 115]));
        await db.SaveChangesAsync(Ct);
        var calc = Calculator(db); var rec = new CarRecommendationService(db);
        await calc.ComputeAndCacheAsync(1, 0, 1, graph.CustomerId, RaceOnly, graph.UserId, Ct);
        await calc.ComputeAndCacheAsync(1, 1, 1, graph.CustomerId, RaceOnly, graph.UserId, Ct);
        Assert.Equal(40, Assert.Single(await rec.GetRecommendationsAsync(1, 1, graph.CustomerId, RaceOnly, graph.UserId, Ct)).ExpectedPercentile);
        await RewriteAsync(db, 2, rows => db.SubsessionResults.Remove(rows.Single(r => r.BestLapSeconds == 115)));
        var small = await calc.ComputeAndCacheAsync(1, 1, 1, graph.CustomerId, RaceOnly, graph.UserId, Ct);
        Assert.Equal(4, small!.SampleSize);
        Assert.Single(await db.ClassifiedCarPercentileResults.ToListAsync(Ct));
        var under = Assert.Single(await rec.GetRecommendationsAsync(1, 1, graph.CustomerId, RaceOnly, graph.UserId, Ct));
        Assert.False(under.IsPercentilePresentable); Assert.Equal(50, under.ExpectedPercentile);
        Assert.Single(await db.ClassifiedCarPercentileResults.ToListAsync(Ct));
        await RewriteAsync(db, 2, rows => db.SubsessionResults.Add(new() { SubsessionId = 2, CustId = 9999,
            CarId = 1, CarClassId = 1, BestLapSeconds = 120 }));
        Assert.Equal(40, Assert.Single(await rec.GetRecommendationsAsync(1, 1, graph.CustomerId, RaceOnly, graph.UserId, Ct)).ExpectedPercentile);
        Assert.Equal(40, Assert.Single(await rec.GetRecommendationsAsync(1, 1, graph.CustomerId, RaceOnly, graph.UserId, Ct)).ExpectedPercentile);
        Assert.Equal(2, await db.ClassifiedCarPercentileResults.CountAsync(Ct));
        await RewriteAsync(db, 2, rows => db.SubsessionResults.Remove(rows.Single(r => r.CustId == graph.CustomerId)));
        Assert.Null(await calc.ComputeAndCacheAsync(1, 1, 1, graph.CustomerId, RaceOnly, graph.UserId, Ct));
        var projected = Assert.Single(await rec.GetRecommendationsAsync(1, 1, graph.CustomerId, RaceOnly, graph.UserId, Ct));
        Assert.Null(projected.PercentileRank); Assert.Null(projected.BestLapSeconds);
        Assert.Equal(50, projected.ExpectedPercentile);
        Assert.Equal(graph.Week.Id, Assert.Single(await db.ClassifiedCarPercentileResults.ToListAsync(Ct)).WeekId);
    }

    [Fact]
    public async Task WrongTrackCannotChangeEitherApiWritersFieldOrPersonalBest()
    {
        await using var db = await DemoAsync();
        var graph = await SeedAsync(db, DemoData.DriverCustId);
        db.Tracks.Add(new() { Id = 102, Name = "Synthetic shared venue", ConfigName = "Other layout" });
        var wrong = new Subsession { Id = 3, SeasonId = 10, WeekId = graph.Week.Id, TrackId = 102, RaceWeekIndex = 0 };
        var wrongRows = Field(wrong, graph.CustomerId, [2, 3, 4, 5]).ToArray();
        wrongRows.Single(r => r.CustId == graph.CustomerId).BestLapSeconds = 1;
        db.SubsessionResults.AddRange(wrongRows);
        await db.SaveChangesAsync(Ct);
        var computed = await Calculator(db).ComputeAndCacheAsync(1, 0, 1, graph.CustomerId, RaceOnly, graph.UserId, Ct);
        Assert.Equal(50, computed!.PercentileRank); Assert.Equal(5, computed.SampleSize);
        var rec = Assert.Single(await new CarRecommendationService(db).GetRecommendationsAsync(1, 0, graph.CustomerId, RaceOnly, graph.UserId, Ct));
        Assert.Equal(50, rec.PercentileRank); Assert.Equal(5, rec.FieldSize); Assert.Equal(110, rec.BestLapSeconds);
        Assert.Single(await db.ClassifiedCarPercentileResults.ToListAsync(Ct));
    }

    [Fact]
    public async Task QuarantinedTypesDatesAndTrackIdsNeverBecomeAuthorizedEvidence()
    {
        await using var db = await fixture.CreateDbContextAsync(Ct);
        var graph = await SeedAsync(db);
        db.Tracks.Add(new() { Id = 102, Name = "Synthetic shared venue", ConfigName = "Other layout" });
        AddUpload(db, graph, 1, LapSessionType.Unknown, trackId: 102);
        AddUpload(db, graph, 2, LapSessionType.Race, at: graph.Week.EndTime!.Value);
        AddUpload(db, graph, 3, LapSessionType.Practice);
        await db.SaveChangesAsync(Ct);
        foreach (var evidence in new[] { RaceOnly, Uploaded, PersonalBestEvidence.FromRequest(true, [LapSessionType.Race]) })
        {
            var result = await Calculator(db).ComputeAndCacheAsync(1, 0, 1, graph.CustomerId, evidence, graph.UserId, Ct);
            Assert.Equal(110, result!.YourBestLapSeconds); Assert.Null(result.UploadedBestOutsideWeek);
            Assert.Equal(LapEvidence.RaceLap, result.YourBestLapEvidence);
        }
        Assert.Empty(await db.ClassifiedCarPercentileResults.ToListAsync(Ct));
        Assert.Empty(await db.CarPercentileResults.ToListAsync(Ct));
    }

    [Fact]
    public async Task RecommendationsAverageDistinctWeeksAcrossSeasonsThroughRequestedDateOnly()
    {
        await using var db = await DemoAsync();
        var graph = await SeedAsync(db, DemoData.DriverCustId);
        db.Seasons.AddRange(new Season { Id = 9, SeriesId = 1, Year = 2026, Quarter = 3 },
            new Season { Id = 11, SeriesId = 1, Year = 2027, Quarter = 1, Active = true });
        void Add(int id, int season, int index, int days, double[] others)
        {
            var week = new Week { Id = Guid.NewGuid(), SeasonId = season, RaceWeekIndex = index, TrackId = 101,
                StartDate = graph.Week.StartDate.AddDays(days) };
            db.Weeks.Add(week);
            var race = new Subsession { Id = id, SeasonId = season, RaceWeekIndex = index,
                WeekId = week.Id, TrackId = 101 };
            db.SubsessionResults.AddRange(Field(race, graph.CustomerId, others));
        }
        Add(9, 9, 0, -14, [120, 125, 130, 135]); // 90, earlier Season
        Add(10, 9, 1, 0, [90, 100, 105, 115]);  // 30, distinct Week on requested date
        Add(11, 11, 0, 21, [80, 90, 100, 105]); // 10, future Season cannot contribute
        await db.SaveChangesAsync(Ct);
        var result = Assert.Single(await new CarRecommendationService(db).GetRecommendationsAsync(
            1, 0, graph.CustomerId, RaceOnly, graph.UserId, Ct));
        Assert.Equal(50, result.PercentileRank);
        Assert.Equal(170.0 / 3, result.ExpectedPercentile!.Value, 10);
        var repeated = Assert.Single(await new CarRecommendationService(db).GetRecommendationsAsync(
            1, 0, graph.CustomerId, RaceOnly, graph.UserId, Ct));
        Assert.Equal(result.ExpectedPercentile, repeated.ExpectedPercentile);
        Assert.Equal(4, await db.ClassifiedCarPercentileResults.CountAsync(Ct));
    }

    [Fact]
    public async Task RecommendationChronologyUsesDateWitnessedByRefreshAfterCallerPreRead()
    {
        var interceptor = new CorrectRequestedDate();
        var options = await fixture.CreateOptionsAsync(Ct, interceptor);
        await using var db = new AppDbContext(options, new IRacingDataScope(DataProvenance.Demo));
        DbContextFactory.InstallStoredEvidenceFixtures(db);
        db.FeatureFlags.Add(new() { Key = "iracing-demo", Name = "Synthetic preview", IsEnabled = true, MinimumRole = "Standard" });
        var graph = await SeedAsync(db, DemoData.DriverCustId);
        var other = new Week { Id = Guid.NewGuid(), SeasonId = 10, RaceWeekIndex = 1, TrackId = 101,
            StartDate = graph.Week.StartDate.AddDays(-4) };
        db.Weeks.Add(other);
        db.SubsessionResults.AddRange(Field(new Subsession { Id = 8, SeasonId = 10, WeekId = other.Id,
            TrackId = 101, RaceWeekIndex = 1 }, graph.CustomerId, [120, 125, 130, 135]));
        await db.SaveChangesAsync(Ct);
        interceptor.Correct = async token =>
        {
            var plainOptions = fixture.WithInterceptors(options);
            await using var correction = new AppDbContext(plainOptions, new IRacingDataScope(DataProvenance.Demo));
            await correction.Weeks.Where(w => w.Id == graph.Week.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(w => w.StartDate, graph.Week.StartDate.AddDays(-7)), token);
        };
        var result = Assert.Single(await new CarRecommendationService(db).GetRecommendationsAsync(
            1, 0, graph.CustomerId, RaceOnly, graph.UserId, Ct));
        Assert.True(interceptor.Applied);
        Assert.Equal(50, result.ExpectedPercentile); // Earlier caller date would have included rank90.
        Assert.Equal(2, await db.ClassifiedCarPercentileResults.CountAsync(Ct));
    }

    private sealed class CorrectRequestedDate : DbCommandInterceptor
    {
        public Func<CancellationToken, Task>? Correct { get; set; }
        public bool Applied { get; private set; }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            // Caller projections do not read EndTime; the module's calendar witness does.
            if (!Applied && Correct is not null && command.CommandText.Contains("FROM iracing.\"Weeks\"", StringComparison.Ordinal)
                && command.CommandText.Contains("\"EndTime\"", StringComparison.Ordinal))
            {
                Applied = true;
                await Correct(cancellationToken);
            }
            return result;
        }
    }

    private async Task<AppDbContext> DemoAsync()
    {
        var options = await fixture.CreateOptionsAsync(Ct);
        var db = new AppDbContext(options, new IRacingDataScope(DataProvenance.Demo));
        DbContextFactory.InstallStoredEvidenceFixtures(db);
        db.FeatureFlags.Add(new() { Key = "iracing-demo", Name = "Synthetic preview", IsEnabled = true, MinimumRole = "Standard" });
        await db.SaveChangesAsync(Ct);
        return db;
    }

    private static async Task RewriteAsync(AppDbContext db, int subsession, Action<List<SubsessionResult>> change)
    {
        var writer = await SyntheticEvidenceWriter.OpenAsync(db, Ct);
        var header = await db.Subsessions.SingleAsync(s => s.Id == subsession, Ct);
        var rows = await db.SubsessionResults.Where(r => r.SubsessionId == subsession).ToListAsync(Ct);
        db.Entry(header).State = EntityState.Modified;
        foreach (var row in rows) db.Entry(row).State = EntityState.Modified;
        change(rows);
        await writer.SaveChangesAsync(Ct);
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

}
