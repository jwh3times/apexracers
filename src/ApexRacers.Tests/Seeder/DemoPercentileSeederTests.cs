using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Core.PercentileHistory;
using ApexRacers.Data;
using ApexRacers.Seeder.Demo;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ApexRacers.Tests.Seeder;

[Collection(PostgreSqlCollection.Name)]
public sealed class DemoPercentileSeederTests(PostgreSqlFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task FreshDemoRowsUseActualSubjectAndRaceOnlyWhileLegacyRowsAndOriginalSourceClocksRemainSeparate()
    {
        await using var db = await CreateAsync();
        var users = new[] { Guid.NewGuid(), Guid.NewGuid() };
        foreach (var user in users)
            db.Users.Add(new ApplicationUser { Id = user, DisplayName = "Synthetic User", IRacingCustomerId = 777 + Array.IndexOf(users, user) });
        var week = await FieldAsync(db, includeSubject: true);
        var sourceBefore = await db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(c => c.Kind == EvidenceCopyKind.OfficialField, Ct);
        // A representative fresh compatibility producer remains selection-less and separate.
        var legacyWriter = await SyntheticEvidenceWriter.OpenAsync(db, Ct);
        db.CarPercentileResults.Add(new CarPercentileResult { UserId = users[0], CarId = 1, SeriesId = 1, WeekId = week,
            PercentileRank = 50, TopSharePercent = 60, SampleSize = 5, ComputedAt = DateTimeOffset.UtcNow });
        await legacyWriter.SaveChangesAsync(Ct);
        var legacy = await db.CarPercentileResults.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(2, await new DemoPercentileSeeder(db).SeedAsync(Ct));
        var classified = await db.ClassifiedCarPercentileResults.AsNoTracking().OrderBy(r => r.UserId).ToListAsync(Ct);
        Assert.Equal(2, classified.Count);
        foreach (var row in classified)
        {
            Assert.Contains(row.UserId, users);
            Assert.Equal(DemoData.DriverCustId, row.CustomerId);
            Assert.Equal(PercentileOwnerKind.DemoSubject, row.OwnerKind);
            Assert.Equal("v1:0:0", row.SelectionKey);
            Assert.Equal(1, row.SelectionPolicyVersion);
            Assert.Equal(50, row.PercentileRank);
            Assert.Equal(110, row.PersonalBestLapSeconds);
            Assert.Equal(LapEvidence.RaceLap, row.PersonalBestLapEvidence);
            Assert.Null(row.GrantId);
            var marker = await db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(c => c.Id == row.EvidenceCopyId, Ct);
            Assert.Equal(PercentileHistoryKey.Hash(ClassifiedPercentileContract.Key(row)), marker.KeyHash);
            Assert.Equal(marker.Version, row.ReadingRevision);
            Assert.Equal(marker.PurposeId, row.PreparationPurposeId);
        }
        Assert.Equal(0, await new DemoPercentileSeeder(db).SeedAsync(Ct));
        var after = await db.ClassifiedCarPercentileResults.AsNoTracking().OrderBy(r => r.UserId).ToListAsync(Ct);
        Assert.Equal(classified.Select(r => r.EvidenceCopyId), after.Select(r => r.EvidenceCopyId));
        Assert.Equal(classified.Select(r => r.ComputedAt), after.Select(r => r.ComputedAt));
        Assert.Equal(legacy.EvidenceCopyId, (await db.CarPercentileResults.AsNoTracking().SingleAsync(Ct)).EvidenceCopyId);
        var sourceAfter = await db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(c => c.Id == sourceBefore.Id, Ct);
        Assert.Equal(sourceBefore.OriginalAcquiredAt, sourceAfter.OriginalAcquiredAt);
        Assert.Null(sourceAfter.UnavailableAt);
        Assert.Equal(2, await db.Users.CountAsync(Ct));
        Assert.Equal(777, (await db.Users.FindAsync([users[0]], Ct))!.IRacingCustomerId);
    }

    [Fact]
    public async Task NoSyntheticSubjectBestLeavesNewHistoryAbsentInsteadOfPromotingExistingLegacyReading()
    {
        await using var db = await CreateAsync();
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, DisplayName = "Synthetic User", IRacingCustomerId = 1000 });
        var week = await FieldAsync(db, includeSubject: false);
        var writer = await SyntheticEvidenceWriter.OpenAsync(db, Ct);
        db.CarPercentileResults.Add(new CarPercentileResult { UserId = userId, CarId = 1, SeriesId = 1, WeekId = week,
            PercentileRank = 90, TopSharePercent = 20, SampleSize = 5, ComputedAt = DateTimeOffset.UtcNow });
        await writer.SaveChangesAsync(Ct);
        Assert.Equal(0, await new DemoPercentileSeeder(db).SeedAsync(Ct));
        Assert.Empty(await db.ClassifiedCarPercentileResults.IgnoreQueryFilters().ToListAsync(Ct));
        Assert.Single(await db.CarPercentileResults.ToListAsync(Ct));
    }

    [Fact]
    public async Task CorrectedSyntheticFieldRebuildsInvalidatedIdentityWithoutPromotingOrRetimingOldCopies()
    {
        await using var db = await CreateAsync();
        db.Users.Add(new ApplicationUser { Id = Guid.NewGuid(), DisplayName = "Synthetic User", IRacingCustomerId = 777 });
        var week = await FieldAsync(db, includeSubject: true);
        Assert.Equal(1, await new DemoPercentileSeeder(db).SeedAsync(Ct));
        var old = await db.ClassifiedCarPercentileResults.AsNoTracking().SingleAsync(Ct);
        var originalMarker = await db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(c => c.Id == old.EvidenceCopyId, Ct);
        var fieldMarker = await db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(c => c.Kind == EvidenceCopyKind.OfficialField, Ct);
        await using var correction = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(db.Database.GetConnectionString()).Options, new IRacingDataScope(DataProvenance.Demo));
        var copies = new EvidenceCopyLifecycle(correction, TimeProvider.System);
        var receipt = await copies.CaptureAsync(fieldMarker.PurposeId, EvidenceCopyKind.OfficialField, "seed-test-field", ct: Ct);
        var results = new[] { 90d, 100d, 105d, 115d, 120d }.Select((lap, i) => new SubsessionResult
        { SubsessionId = 1, CustId = i == 0 ? DemoData.DriverCustId : 1000 + i, CarId = 1, CarClassId = 1, BestLapSeconds = lap }).ToList();
        await copies.CommitAsync(receipt, new OfficialFieldBatch(new() { Id = 1, SeasonId = 1, WeekId = week,
            TrackId = 1, OfficialSession = true, StartTime = DateTimeOffset.UtcNow }, results), Ct);
        Assert.Empty(await db.ClassifiedCarPercentileResults.AsNoTracking().ToListAsync(Ct));
        Assert.Equal(1, await new DemoPercentileSeeder(db).SeedAsync(Ct));
        var fresh = await db.ClassifiedCarPercentileResults.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(90, fresh.PersonalBestLapSeconds);
        Assert.Equal(90, fresh.PercentileRank);
        Assert.NotEqual(old.EvidenceCopyId, fresh.EvidenceCopyId);
        var retired = await db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(c => c.Id == old.EvidenceCopyId, Ct);
        Assert.Equal(originalMarker.OriginalAcquiredAt, retired.OriginalAcquiredAt);
        Assert.NotNull(retired.UnavailableAt);
        Assert.NotNull(retired.VerifiedRemovedAt);
        Assert.Equal(0, await new DemoPercentileSeeder(db).SeedAsync(Ct));
    }

    [Fact]
    public async Task SameWeekWrongLayoutCannotChangeClassifiedBestRankOrSourceSelection()
    {
        await using var db = await CreateAsync();
        db.Users.Add(new ApplicationUser { Id = Guid.NewGuid(), DisplayName = "Synthetic User", IRacingCustomerId = 777 });
        var week = await FieldAsync(db, includeSubject: true);
        db.Tracks.Add(new() { Id = 2, Name = "Synthetic Track", ConfigName = "Other layout" });
        await db.SaveChangesAsync(Ct);
        var copies = new EvidenceCopyLifecycle(db, TimeProvider.System);
        var purpose = await copies.OpenSyntheticPurposeAsync(ct: Ct);
        var receipt = await copies.CaptureAsync(purpose, EvidenceCopyKind.OfficialField, "same-week-wrong-layout", ct: Ct);
        var wrongSource = await copies.CommitAsync(receipt, new OfficialFieldBatch(new() { Id = 2, SeasonId = 1, WeekId = week,
            TrackId = 2, OfficialSession = true, StartTime = DateTimeOffset.UtcNow },
            [new() { SubsessionId = 2, CustId = DemoData.DriverCustId, CarId = 1, CarClassId = 1, BestLapSeconds = 1 },
             new() { SubsessionId = 2, CustId = 999999, CarId = 1, CarClassId = 1, BestLapSeconds = 2 }]), Ct);
        Assert.Equal(1, await new DemoPercentileSeeder(db).SeedAsync(Ct));
        var reading = await db.ClassifiedCarPercentileResults.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(110, reading.PersonalBestLapSeconds);
        Assert.Equal(50, reading.PercentileRank);
        Assert.Equal(5, reading.SampleSize);
        var dependencies = await db.EvidenceCopyDependencies.Where(d => d.CopyId == reading.EvidenceCopyId).ToListAsync(Ct);
        Assert.Single(dependencies);
        Assert.DoesNotContain(dependencies, d => d.SourceCopyId == wrongSource.CopyId);
        Assert.Equal(0, await new DemoPercentileSeeder(db).SeedAsync(Ct));
    }

    private async Task<AppDbContext> CreateAsync()
    {
        var options = await postgres.CreateOptionsAsync(Ct);
        await using (var setup = new AppDbContext(options))
        { await setup.Database.EnsureDeletedAsync(Ct); await setup.Database.MigrateAsync(Ct); }
        return new(options, new IRacingDataScope(DataProvenance.Demo));
    }

    private static async Task<Guid> FieldAsync(AppDbContext db, bool includeSubject)
    {
        db.Series.Add(new() { Id = 1, Name = "Synthetic Series" });
        db.Seasons.Add(new() { Id = 1, SeriesId = 1, Active = true });
        db.Cars.Add(new() { Id = 1, Name = "Synthetic Car", NameAbbreviated = "SC" });
        db.CarClasses.Add(new() { Id = 1, Name = "Synthetic Class", ShortName = "SC" });
        db.Tracks.Add(new() { Id = 1, Name = "Synthetic Track" });
        var week = Guid.NewGuid();
        db.Weeks.Add(new() { Id = week, SeasonId = 1, TrackId = 1, StartDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1) });
        await db.SaveChangesAsync(Ct);
        var copies = new EvidenceCopyLifecycle(db, TimeProvider.System);
        var purpose = await copies.OpenSyntheticPurposeAsync(ct: Ct);
        var receipt = await copies.CaptureAsync(purpose, EvidenceCopyKind.OfficialField, "seed-test-field", ct: Ct);
        var result = new[] { 110d, 100d, 105d, 115d, 120d }.Select((lap, i) => new SubsessionResult
        { SubsessionId = 1, CustId = i == 0 && includeSubject ? DemoData.DriverCustId : 1000 + i, CarId = 1, CarClassId = 1, BestLapSeconds = lap }).ToList();
        await copies.CommitAsync(receipt, new OfficialFieldBatch(new() { Id = 1, SeasonId = 1, WeekId = week,
            TrackId = 1, OfficialSession = true, StartTime = DateTimeOffset.UtcNow }, result), Ct);
        return week;
    }
}
