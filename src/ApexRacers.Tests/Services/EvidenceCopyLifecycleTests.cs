using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using ApexRacers.Seeder;
using ApexRacers.Seeder.Demo;
using ApexRacers.Seeder.Verification;
using ApexRacers.Ingestion;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ApexRacers.Api.Services;
using System.Data.Common;
using NSubstitute;
using Npgsql;
using Xunit;

namespace ApexRacers.Tests.Services;

[Collection(PostgreSqlCollection.Name)]
public sealed class EvidenceCopyLifecycleTests(PostgreSqlFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MigratedDatabaseRejectsOldWritersMarkerReuseAndPreparedWriteAfterRawDeletion()
    {
        var options = await MigratedOptionsAsync();
        var clock = new CopyClock();
        await using var db = Demo(options);
        var lifecycle = new EvidenceCopyLifecycle(db, clock);
        var purpose = await lifecycle.OpenSyntheticPurposeAsync(ct: Ct);
        var receipt = await lifecycle.CaptureAsync(purpose, EvidenceCopyKind.MappedCache, "cache", ct: Ct);
        var source = await lifecycle.CommitAsync(receipt, Cache(receipt), Ct);
        var delayed = await lifecycle.CaptureAsync(purpose, EvidenceCopyKind.MappedCache, "cache", ct: Ct);
        var reused = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE iracing.\"MappedDataCaches\" SET \"Payload\" = 'resurrection'", Ct));
        Assert.Equal(PostgresErrorCodes.CheckViolation, reused.SqlState);
        var oldWriter = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("""
            INSERT INTO iracing."MappedDataCaches" ("Provenance", "CacheKey", "Payload", "FetchedAt", "ExpiresAt")
            VALUES (2, 'old-writer', 'payload', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP + interval '1 hour')
            """, Ct));
        Assert.Equal(PostgresErrorCodes.CheckViolation, oldWriter.SqlState);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM iracing.\"MappedDataCaches\"", Ct);
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => lifecycle.CommitAsync(delayed, Cache(delayed), Ct));
        Assert.Empty(await db.ExternalDataCaches.IgnoreQueryFilters().ToListAsync(Ct));
        Assert.Equal(source.Version, (await db.EvidenceCopyMarkers.SingleAsync(c => c.Id == source.CopyId, Ct)).Version);
    }

    [Fact]
    public async Task NamesHaveTwentyFourHourClockWhileOfficialFieldSurvivesOwnerWithdrawalAndSeasonEnd()
    {
        var options = await MigratedOptionsAsync();
        var clock = new CopyClock();
        await using var db = Demo(options);
        db.Series.Add(new() { Id = 1, Name = "Synthetic Series" });
        db.Seasons.Add(new() { Id = 1, SeriesId = 1, Active = true });
        db.Tracks.Add(new() { Id = 1, Name = "Synthetic Track" });
        db.Cars.Add(new() { Id = 1, Name = "Synthetic Car", NameAbbreviated = "SC" });
        db.CarClasses.Add(new() { Id = 1, Name = "Synthetic Class", ShortName = "SC" });
        var user = new ApplicationUser { Id = Guid.NewGuid(), DisplayName = "Local account label" };
        db.Users.Add(user);
        await db.SaveChangesAsync(Ct);
        var journal = Substitute.For<IDriverEnforcementJournal>();
        journal.ReadUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new DriverUserEnforcement(true));
        journal.ReadAsync(Arg.Any<DriverScope>(), Arg.Any<CancellationToken>()).Returns(new DriverJournalState(true, 0, []));
        var store = new DriverAuthorityStore(db, clock);
        var scope = new DriverScope(user.Id, 42001, DataProvenance.Demo);
        var access = await store.GrantAsync(new(Guid.NewGuid(), scope, clock.GetUtcNow().AddDays(-1), "synthetic-test-only", "Synthetic Driver"),
            new(DriverAuthorizationPolicy.PersonalConsentVersion), journal, Ct);
        var lifecycle = new EvidenceCopyLifecycle(db, clock, journal);
        var personal = await lifecycle.OpenSyntheticPurposeAsync(EvidencePurposeKind.Personal, access: access, ct: Ct);
        var named = await lifecycle.CaptureAsync(personal, EvidenceCopyKind.AuthorizedName, "owner-name", ct: Ct);
        await lifecycle.CommitAsync(named, new AuthorizedNameBatch(access.GrantId), Ct);
        var official = await lifecycle.OpenSyntheticPurposeAsync(EvidencePurposeKind.IndependentOfficial, seasonId: 1, ct: Ct);
        var field = await lifecycle.CaptureAsync(official, EvidenceCopyKind.OfficialField, "field:1", ct: Ct);
        var fieldSource = await lifecycle.CommitAsync(field, new OfficialFieldBatch(new()
        {
            Id = 1,
            SeasonId = 1,
            TrackId = 1,
            OfficialSession = true,
            TeamEntryCount = 0,
            AiEntryCount = 0
        },
            [new() { SubsessionId = 1, CustId = 42001, CarId = 1, CarClassId = 1, DisplayName = "Synthetic Driver" },
             new() { SubsessionId = 1, CustId = 42002, CarId = 1, CarClassId = 1, DisplayName = "Another Driver" }]), Ct);
        Assert.Equal(new long[] { 42001, 42002 }, await db.SubsessionResults.OrderBy(r => r.CustId).Select(r => r.CustId).ToArrayAsync(Ct));
        Assert.All(await db.SubsessionResults.ToListAsync(Ct), r => Assert.Null(r.DisplayName));
        var foreignOwner = await lifecycle.CaptureAsync(personal, EvidenceCopyKind.PersonalDerivative, "foreign-owner", [fieldSource], Ct);
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => lifecycle.CommitAsync(foreignOwner,
            new PercentileBatch(new() { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), CarId = 1, SeriesId = 1, WeekId = Guid.NewGuid() }), Ct));
        Assert.Empty(await db.CarPercentileResults.IgnoreQueryFilters().ToListAsync(Ct));
        var privateReceipt = await lifecycle.CaptureAsync(personal, EvidenceCopyKind.MappedCache, "private-source", ct: Ct);
        var privateSource = await lifecycle.CommitAsync(privateReceipt, Cache(privateReceipt, TimeSpan.FromDays(10)), Ct);
        var preview = await lifecycle.OpenSyntheticPurposeAsync(ct: Ct);
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => lifecycle.CaptureAsync(preview,
            EvidenceCopyKind.MappedCache, "laundered-source", [privateSource], Ct));
        var delayedName = await lifecycle.CaptureAsync(personal, EvidenceCopyKind.AuthorizedName, "owner-name", ct: Ct);
        clock.Advance(TimeSpan.FromMinutes(1));
        var loss = clock.GetUtcNow();
        await store.ApplyIntentAsync(new(Guid.NewGuid(), access.GrantId, scope, DriverLifecycleKind.WithdrawPersonal, loss), Ct);
        Assert.Empty(await db.AuthorizedDriverNameCopies.ToListAsync(Ct));
        Assert.Equal(loss.AddHours(24), (await db.EvidenceCopyMarkers.SingleAsync(c => c.Kind == EvidenceCopyKind.AuthorizedName, Ct)).RemovalDueAt);
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => lifecycle.CommitAsync(delayedName, new AuthorizedNameBatch(access.GrantId), Ct));
        db.ChangeTracker.Clear();
        var season = await db.Seasons.SingleAsync(Ct);
        season.Active = false;
        await db.SaveChangesAsync(Ct);
        Assert.Equal(2, await db.SubsessionResults.CountAsync(Ct));
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => lifecycle.CaptureAsync(official, EvidenceCopyKind.OfficialField, "field:2", ct: Ct));
        await lifecycle.ReconcileAsync(Ct);
        Assert.Empty(await db.AuthorizedDriverNameCopies.IgnoreQueryFilters().ToListAsync(Ct));
        Assert.Equal(2, await db.SubsessionResults.CountAsync(Ct));
        await lifecycle.EndPurposeAsync(official, loss, Ct);
        Assert.Equal(loss.AddDays(7), (await db.EvidenceCopyMarkers.SingleAsync(c => c.Kind == EvidenceCopyKind.OfficialField, Ct)).RemovalDueAt);
        await lifecycle.ReconcileAsync(Ct);
        Assert.Empty(await db.SubsessionResults.IgnoreQueryFilters().ToListAsync(Ct));
        Assert.Empty(await db.Subsessions.IgnoreQueryFilters().ToListAsync(Ct));
    }

    private async Task<DbContextOptions<AppDbContext>> MigratedOptionsAsync()
    {
        var options = await postgres.CreateOptionsAsync(Ct);
        await using var db = Demo(options);
        await db.Database.EnsureDeletedAsync(Ct);
        await db.Database.MigrateAsync(Ct);
        return options;
    }

    [Fact]
    public async Task HistoricalOwnerRequestEndsCollectionWithoutEndingRetainedNameFreeField()
    {
        var options = await MigratedOptionsAsync();
        var clock = new CopyClock();
        await using var db = Demo(options);
        await CatalogAsync(db);
        var journal = Substitute.For<IDriverEnforcementJournal>();
        journal.ReadUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new DriverUserEnforcement(true));
        journal.ReadAsync(Arg.Any<DriverScope>(), Arg.Any<CancellationToken>()).Returns(new DriverJournalState(true, 0, []));
        var user = new ApplicationUser { Id = Guid.NewGuid(), DisplayName = "Synthetic owner" };
        db.Users.Add(user);
        await db.SaveChangesAsync(Ct);
        var store = new DriverAuthorityStore(db, clock);
        var scope = new DriverScope(user.Id, 42001, DataProvenance.Demo);
        var access = await store.GrantAsync(new(Guid.NewGuid(), scope, clock.GetUtcNow().AddDays(-1), "synthetic-test", "Synthetic Driver"),
            new(DriverAuthorizationPolicy.PersonalConsentVersion), journal, Ct);
        var lifecycle = new EvidenceCopyLifecycle(db, clock, journal);
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => lifecycle.OpenSyntheticPurposeAsync(
            EvidencePurposeKind.AuthorizedHistory, 1, access, ct: Ct));
        (await db.Seasons.SingleAsync(Ct)).Active = false;
        await db.SaveChangesAsync(Ct);
        var requestId = Guid.NewGuid();
        var purpose = await lifecycle.OpenSyntheticPurposeAsync(EvidencePurposeKind.AuthorizedHistory, 1, access, requestId, Ct);
        var receipt = await lifecycle.CaptureAsync(purpose, EvidenceCopyKind.OfficialField, "history:1", ct: Ct);
        await lifecycle.CommitAsync(receipt, Field(1), Ct);
        var delayed = await lifecycle.CaptureAsync(purpose, EvidenceCopyKind.OfficialField, "history:2", ct: Ct);
        await lifecycle.CompleteHistoricalRequestAsync(purpose, requestId, Ct);
        clock.Advance(TimeSpan.FromHours(1));
        await lifecycle.CompleteHistoricalRequestAsync(purpose, requestId, Ct);
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => lifecycle.CommitAsync(delayed, Field(2), Ct));
        db.ChangeTracker.Clear();
        await store.ApplyIntentAsync(new(Guid.NewGuid(), access.GrantId, scope, DriverLifecycleKind.WithdrawPersonal, clock.GetUtcNow()), Ct);
        Assert.Equal(2, await db.SubsessionResults.CountAsync(Ct));
        Assert.All(await db.SubsessionResults.ToListAsync(Ct), row => Assert.Null(row.DisplayName));
        Assert.Null((await db.EvidencePurposes.SingleAsync(p => p.Id == purpose, Ct)).OriginalEndedAt);
        Assert.Equal(0, (await lifecycle.ReconcileAsync(Ct)).VerifiedRemoved);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RawWeatherRemovalInvalidatesDescendantsAndPreparedWrites(bool deleteWeek)
    {
        var options = await MigratedOptionsAsync();
        var clock = new CopyClock();
        await using var db = Demo(options);
        var week = await CatalogAsync(db);
        var lifecycle = new EvidenceCopyLifecycle(db, clock);
        var official = await lifecycle.OpenSyntheticPurposeAsync(EvidencePurposeKind.IndependentOfficial, 1, ct: Ct);
        var receipt = await lifecycle.CaptureAsync(official, EvidenceCopyKind.Weather, "weather", ct: Ct);
        var source = await lifecycle.CommitAsync(receipt, new WeatherBatch(week, "{}"), Ct);
        var preview = await lifecycle.OpenSyntheticPurposeAsync(ct: Ct);
        var derived = await lifecycle.CaptureAsync(preview, EvidenceCopyKind.MappedCache, "weather-derived", [source], Ct);
        await lifecycle.CommitAsync(derived, Cache(derived, TimeSpan.FromDays(10)), Ct);
        var delayed = await lifecycle.CaptureAsync(official, EvidenceCopyKind.Weather, "late-weather", ct: Ct);
        await db.Database.ExecuteSqlRawAsync(deleteWeek ? "DELETE FROM iracing.\"Weeks\""
            : "UPDATE iracing.\"Weeks\" SET \"DemoWeatherSummaryJson\" = NULL", Ct);
        Assert.Empty(await db.ExternalDataCaches.ToListAsync(Ct));
        Assert.Single(await db.ExternalDataCaches.IgnoreQueryFilters().ToListAsync(Ct));
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => lifecycle.CommitAsync(delayed, new WeatherBatch(week, "{}"), Ct));
        Assert.Equal(2, (await lifecycle.ReconcileAsync(Ct)).VerifiedRemoved);
        Assert.Empty(await db.ExternalDataCaches.IgnoreQueryFilters().ToListAsync(Ct));
        Assert.False(await db.Weeks.AnyAsync(w => w.DemoWeatherSummaryJson != null || w.DemoWeatherEvidenceCopyId != null, Ct));
    }

    [Fact]
    public async Task SyntheticCalculationCannotDropOneErasedContributingField()
    {
        var options = await MigratedOptionsAsync();
        var clock = new CopyClock();
        await using var db = Demo(options);
        var week = await CatalogAsync(db);
        var user = new ApplicationUser { Id = Guid.NewGuid(), DisplayName = "Synthetic User" };
        db.Users.Add(user);
        await db.SaveChangesAsync(Ct);
        var lifecycle = new EvidenceCopyLifecycle(db, clock);
        foreach (var id in new[] { 1, 2 })
        {
            var official = await lifecycle.OpenSyntheticPurposeAsync(EvidencePurposeKind.IndependentOfficial, 1, ct: Ct);
            var receipt = await lifecycle.CaptureAsync(official, EvidenceCopyKind.OfficialField, $"field:{id}", ct: Ct);
            var field = Field(id);
            field.Race.WeekId = week;
            await lifecycle.CommitAsync(receipt, field, Ct);
        }
        var writer = await SyntheticEvidenceWriter.OpenAsync(db, Ct, clock);
        Assert.Equal(4, await db.SubsessionResults.CountAsync(Ct)); // Calculation input has both Fields.
        await using (var removal = Demo(options))
            await removal.Database.ExecuteSqlRawAsync("DELETE FROM iracing.\"RaceEvidenceSubsessions\" WHERE \"Id\" = 1", Ct);
        db.CarPercentileResults.Add(new()
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            CarId = 1,
            SeriesId = 1,
            WeekId = week,
            PercentileRank = 50,
            SampleSize = 2,
            ComputedAt = clock.GetUtcNow()
        });
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => writer.SaveChangesAsync(Ct));
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => writer.SaveChangesAsync(Ct)); // Failed sessions cannot retry attachment.
        Assert.Empty(await db.CarPercentileResults.IgnoreQueryFilters().ToListAsync(Ct));
        Assert.Equal(2, await db.SubsessionResults.CountAsync(Ct));
    }

    [Fact]
    public async Task MigratedSyntheticSeederRoundTripTeardownAndExplicitFreshSession()
    {
        var options = await MigratedOptionsAsync();
        await using var db = Demo(options);
        await new CiCatalogSeeder(db).SeedAsync();
        await new DemoCacheSeeder(db).SeedAllAsync(Ct);
        Assert.All(await DemoSeedVerifier.VerifyDemoAsync(db, Ct), check => Assert.True(check.Passed, check.Name + ": " + check.Detail));
        var stale = await SyntheticEvidenceWriter.OpenAsync(db, Ct);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "ApexRacers.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var purge = await File.ReadAllTextAsync(Path.Combine(root.FullName, "src", "ApexRacers.Data", "Seeds", "purge_demo_data.sql"), Ct);
        await db.Database.ExecuteSqlRawAsync(purge, Ct);
        db.ChangeTracker.Clear();
        Assert.All(await DemoSeedVerifier.VerifyTeardownAsync(db, Ct), check => Assert.True(check.Passed, check.Name));
        db.ExternalDataCaches.Add(new()
        {
            CacheKey = "late-seeder",
            Payload = "{}",
            FetchedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        });
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => stale.SaveChangesAsync(Ct));
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => new CiCatalogSeeder(db).SeedAsync());
        await new EvidenceCopyLifecycle(db, TimeProvider.System).StartFreshSyntheticPreviewAsync(Ct);
        await new CiCatalogSeeder(db).SeedAsync();
        await new DemoCacheSeeder(db).SeedAllAsync(Ct);
        Assert.All(await DemoSeedVerifier.VerifyDemoAsync(db, Ct), check => Assert.True(check.Passed, check.Name + ": " + check.Detail));
        Assert.Equal(2, await db.EvidencePurposes.CountAsync(Ct));
    }

    private static async Task<Guid> CatalogAsync(AppDbContext db)
    {
        db.Series.Add(new() { Id = 1, Name = "Synthetic Series" });
        db.Seasons.Add(new() { Id = 1, SeriesId = 1, Active = true });
        db.Tracks.Add(new() { Id = 1, Name = "Synthetic Track" });
        db.Cars.Add(new() { Id = 1, Name = "Synthetic Car", NameAbbreviated = "SC" });
        db.CarClasses.Add(new() { Id = 1, Name = "Synthetic Class", ShortName = "SC" });
        var week = Guid.NewGuid();
        db.Weeks.Add(new() { Id = week, SeasonId = 1, TrackId = 1 });
        await db.SaveChangesAsync(Ct);
        return week;
    }

    [Fact]
    public async Task CleanupFailureReportsOverdueWithoutFalseErasureAndRestartPreservesDeadline()
    {
        var options = await MigratedOptionsAsync();
        var clock = new CopyClock();
        Guid copyId;
        await using (var db = Demo(options))
        {
            var lifecycle = new EvidenceCopyLifecycle(db, clock);
            var purpose = await lifecycle.OpenSyntheticPurposeAsync(ct: Ct);
            var receipt = await lifecycle.CaptureAsync(purpose, EvidenceCopyKind.MappedCache, "expiring", ct: Ct);
            copyId = (await lifecycle.CommitAsync(receipt, Cache(receipt), Ct)).CopyId;
        }
        clock.Advance(TimeSpan.FromHours(49));
        var faultOptions = postgres.WithInterceptors(options, new FailedDelete());
        await using (var failed = Demo(faultOptions))
        {
            var lifecycle = new EvidenceCopyLifecycle(failed, clock);
            await Assert.ThrowsAsync<InvalidOperationException>(() => lifecycle.ReconcileAsync(Ct));
            Assert.Empty(failed.ChangeTracker.Entries());
            var outcome = await lifecycle.InspectAsync(Ct);
            Assert.Equal(1, outcome.Pending);
            Assert.Equal(1, outcome.Overdue);
            Assert.Equal(0, outcome.VerifiedRemoved);
            Assert.Single(await failed.ExternalDataCaches.IgnoreQueryFilters().ToListAsync(Ct));
        }
        await using var restarted = Demo(options);
        Assert.Equal(1, (await new EvidenceCopyLifecycle(restarted, clock).ReconcileAsync(Ct)).VerifiedRemoved);
        var marker = await restarted.EvidenceCopyMarkers.SingleAsync(c => c.Id == copyId, Ct);
        Assert.Equal(marker.ExpiresAt!.Value.AddHours(48), marker.RemovalDueAt);
        Assert.NotNull(marker.VerifiedRemovedAt);
        Assert.Empty(await restarted.ExternalDataCaches.IgnoreQueryFilters().ToListAsync(Ct));
    }

    [Fact]
    public async Task ProofExplanationExpiresAtTwelveMonthsWhileMinimalBindingRemains()
    {
        var options = await MigratedOptionsAsync();
        var clock = new CopyClock();
        await using var db = Demo(options);
        var old = new DriverProofReceipt
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            CustomerId = 42001,
            Provenance = DataProvenance.Demo,
            VerifiedAt = clock.GetUtcNow().AddMonths(-12),
            Authority = "synthetic-explanation"
        };
        var recent = new DriverProofReceipt
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            CustomerId = 42002,
            Provenance = DataProvenance.Demo,
            VerifiedAt = clock.GetUtcNow().AddMonths(-12).AddHours(1),
            Authority = "recent-explanation"
        };
        db.Users.AddRange(new ApplicationUser { Id = old.UserId, DisplayName = "Synthetic owner" },
            new ApplicationUser { Id = recent.UserId, DisplayName = "Other synthetic owner" });
        db.DriverProofReceipts.AddRange(old, recent);
        await db.SaveChangesAsync(Ct);
        var store = new DriverAuthorityStore(db, clock);
        Assert.Equal(1, await store.RemoveExpiredExplanationsAsync(Ct));
        db.ChangeTracker.Clear();
        Assert.Equal(string.Empty, (await db.DriverProofReceipts.SingleAsync(r => r.Id == old.Id, Ct)).Authority);
        Assert.Equal("recent-explanation", (await db.DriverProofReceipts.SingleAsync(r => r.Id == recent.Id, Ct)).Authority);
        Assert.Equal(0, await store.RemoveExpiredExplanationsAsync(Ct));
        Assert.Equal(2, await db.DriverProofReceipts.CountAsync(Ct));
    }

    private sealed class FailedDelete : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("DELETE", StringComparison.Ordinal)) throw new InvalidOperationException("Synthetic cleanup failure");
            return ValueTask.FromResult(result);
        }
    }
    private static OfficialFieldBatch Field(int id) => new(new() { Id = id, SeasonId = 1, TrackId = 1, OfficialSession = true },
        [new() { SubsessionId = id, CustId = 42001, CarId = 1, CarClassId = 1, DisplayName = "Synthetic Driver" },
         new() { SubsessionId = id, CustId = 42002, CarId = 1, CarClassId = 1, DisplayName = "Other Synthetic Driver" }]);

    [Fact]
    public async Task IngestionMappedFullFieldCannotCommitAfterPurposeClosureAndRestart()
    {
        var options = await MigratedOptionsAsync();
        var clock = new CopyClock();
        await using var db = Demo(options);
        var week = await CatalogAsync(db);
        var lifecycle = new EvidenceCopyLifecycle(db, clock);
        var purpose = await lifecycle.OpenSyntheticPurposeAsync(EvidencePurposeKind.IndependentOfficial, 1, ct: Ct);
        var first = await lifecycle.CaptureAsync(purpose, EvidenceCopyKind.OfficialField, "field:1", ct: Ct);
        var source = new Aydsko.iRacingData.Results.SubSessionResult
        {
            SeasonId = 1,
            OfficialSession = true,
            Track = new Aydsko.iRacingData.Common.Track { TrackId = 1, TrackName = "Synthetic Track" }
        };
        Aydsko.iRacingData.Results.Result[] rows =
        [new() { CustomerId = 42001, CarId = 1, CarClassId = 1, DisplayName = "Synthetic Driver" },
         new() { CustomerId = 42002, CarId = 1, CarClassId = 1, DisplayName = "Other Synthetic Driver" },
         new() { CustomerId = null, CarId = 1, CarClassId = 1 },
         new() { CustomerId = 42003, AI = true, CarId = 1, CarClassId = 1 }];
        await lifecycle.CommitAsync(first, SubsessionMapper.ToBatch(1, source, week, null, rows, DataProvenance.Demo), Ct);
        Assert.Equal(2, await db.SubsessionResults.CountAsync(Ct));
        Assert.All(await db.SubsessionResults.ToListAsync(Ct), row => Assert.Null(row.DisplayName));
        var stored = await db.Subsessions.SingleAsync(Ct);
        Assert.Equal(1, stored.TeamEntryCount);
        Assert.Equal(1, stored.AiEntryCount);
        var delayed = await lifecycle.CaptureAsync(purpose, EvidenceCopyKind.OfficialField, "field:2", ct: Ct);
        // SDK completion occurs after a distinct process/session closes the original collection.
        await using (var closure = Demo(options))
        {
            var closing = new EvidenceCopyLifecycle(closure, clock);
            await closing.EndPurposeAsync(purpose, clock.GetUtcNow(), Ct);
            await closing.ReconcileAsync(Ct);
        }
        await using var restarted = Demo(options);
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => new EvidenceCopyLifecycle(restarted, clock)
            .CommitAsync(delayed, SubsessionMapper.ToBatch(2, source, week, null, rows, DataProvenance.Demo), Ct));
        Assert.Empty(await restarted.SubsessionResults.IgnoreQueryFilters().ToListAsync(Ct));
        Assert.Empty(await restarted.Subsessions.IgnoreQueryFilters().ToListAsync(Ct));
    }

    [Fact]
    public async Task IngestionScheduleBatchReplacesCopiesAndRejectsLateAcquisition()
    {
        var options = await MigratedOptionsAsync();
        await using var db = Demo(options);
        await CatalogAsync(db);
        var lifecycle = new EvidenceCopyLifecycle(db, TimeProvider.System);
        var purpose = await lifecycle.OpenSyntheticPurposeAsync(EvidencePurposeKind.IndependentOfficial, 1, ct: Ct);
        var item = new Aydsko.iRacingData.Series.SeasonScheduleItem
        {
            RaceWeekNum = 0,
            StartDate = new DateOnly(2026, 10, 5),
            Track = new Aydsko.iRacingData.Common.Track { TrackId = 1, TrackName = "Synthetic Track" },
            Weather = new Aydsko.iRacingData.Series.Weather
            {
                WeatherSummary = new Aydsko.iRacingData.Series.WeatherSummary { TemperatureHigh = 26, TemperatureLow = 20, TemperatureUnits = 1 }
            },
            CarRestrictions = [new() { CarId = 1, WeightPenaltyKg = 12.5m }],
            RaceWeekCars = [new() { CarId = 1, CarName = "Synthetic Car", CarNameAbbreviated = "SC" }]
        };
        var receipt = await lifecycle.CaptureAsync(purpose, EvidenceCopyKind.Weather, "schedule:1", ct: Ct);
        await new SeasonIngest(db).UpsertScheduleAsync(1, [item], Ct, receipt);
        Assert.Single(await db.SeasonCarBops.ToListAsync(Ct));
        Assert.NotNull((await db.Weeks.SingleAsync(Ct)).DemoWeatherEvidenceCopyId);
        receipt = await lifecycle.CaptureAsync(purpose, EvidenceCopyKind.Weather, "schedule:1", ct: Ct);
        item.CarRestrictions[0].WeightPenaltyKg = 15m;
        await new SeasonIngest(db).UpsertScheduleAsync(1, [item], Ct, receipt);
        db.ChangeTracker.Clear();
        Assert.Equal(15, (await db.SeasonCarBops.SingleAsync(Ct)).WeightPenaltyKg);
        var delayed = await lifecycle.CaptureAsync(purpose, EvidenceCopyKind.Weather, "schedule:1", ct: Ct);
        await using (var closure = Demo(options))
            await new EvidenceCopyLifecycle(closure, TimeProvider.System).EndPurposeAsync(purpose, DateTimeOffset.UtcNow, Ct);
        await using var restarted = Demo(options);
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => new SeasonIngest(restarted).UpsertScheduleAsync(1, [item], Ct, delayed));
        Assert.Single(await restarted.SeasonCarBops.IgnoreQueryFilters().ToListAsync(Ct));
        await new EvidenceCopyLifecycle(restarted, TimeProvider.System).ReconcileAsync(Ct);
        Assert.Empty(await restarted.SeasonCarBops.IgnoreQueryFilters().ToListAsync(Ct));
        Assert.Null((await restarted.Weeks.AsNoTracking().SingleAsync(Ct)).DemoWeatherSummaryJson);
    }

    [Fact]
    public async Task UnknownPurposePayloadsStayUnavailableUntilPhysicalErasureWithoutClockRelabeling()
    {
        var options = await postgres.CreateOptionsAsync(Ct);
        await using var db = Demo(options);
        await db.Database.EnsureDeletedAsync(Ct);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261004012741_DriverLifecycleAdmissionSpine", Ct);
        var acquired = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var expiry = acquired.AddDays(1);
        var realPayload = """{"driverName":"Synthetic Legacy Name"}""";
        var unknownPayload = """{"driverName":"Synthetic Quarantined Name"}""";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO iracing."MappedDataCaches" ("Provenance", "CacheKey", "Payload", "FetchedAt", "ExpiresAt")
            VALUES (1, 'legacy-real-purpose-unknown', {realPayload}, {acquired}, {expiry});
            INSERT INTO iracing."QuarantinedDataCaches" ("CacheKey", "Payload", "FetchedAt", "ExpiresAt")
            VALUES ('legacy-unknown-origin', {unknownPayload}, {acquired}, {expiry});
            """, Ct);
        await migrator.MigrateAsync(cancellationToken: Ct);
        Assert.Empty(await db.ExternalDataCaches.ToListAsync(Ct));
        await using (var real = new AppDbContext(options, new IRacingDataScope(DataProvenance.Real)))
            Assert.Empty(await real.ExternalDataCaches.ToListAsync(Ct));
        var retained = await db.ExternalDataCaches.IgnoreQueryFilters().SingleAsync(Ct);
        Assert.Equal(acquired, retained.FetchedAt);
        Assert.Equal(expiry, retained.ExpiresAt);
        Assert.Null(retained.EvidenceCopyId);
        Assert.Equal(DataProvenance.Real, retained.Provenance);
        Assert.Equal(2, await ExternalDataCacheCleanupService.PurgeUnclassifiedAsync(db, Ct));
        Assert.Empty(await db.ExternalDataCaches.IgnoreQueryFilters().ToListAsync(Ct));
        Assert.Empty(await db.QuarantinedDataCaches.ToListAsync(Ct));
        Assert.Empty(await db.EvidencePurposes.ToListAsync(Ct));
        Assert.Equal(0, await ExternalDataCacheCleanupService.PurgeUnclassifiedAsync(db, Ct));
    }

    [Fact]
    public async Task DelayedAcquisitionAndRestartCannotResurrectRemovedCache()
    {
        var options = await postgres.CreateOptionsAsync(Ct);
        var clock = new CopyClock();
        await using var writer = Demo(options);
        var lifecycle = new EvidenceCopyLifecycle(writer, clock);
        var purpose = await lifecycle.OpenSyntheticPurposeAsync(ct: Ct);
        var captured = await lifecycle.CaptureAsync(purpose, EvidenceCopyKind.MappedCache, "synthetic-cache", ct: Ct);
        var committed = await lifecycle.CommitAsync(captured, Cache(captured), Ct);
        var delayed = await lifecycle.CaptureAsync(purpose, EvidenceCopyKind.MappedCache, "synthetic-cache", ct: Ct);

        await using (var closure = Demo(options))
            await new EvidenceCopyLifecycle(closure, clock).EndPurposeAsync(purpose, clock.GetUtcNow(), Ct);
        clock.Advance(TimeSpan.FromHours(1));
        await using (var restart = Demo(options))
        {
            var restarted = new EvidenceCopyLifecycle(restart, clock);
            var outcome = await restarted.ReconcileAsync(Ct);
            Assert.Equal(1, outcome.VerifiedRemoved);
            Assert.False(outcome.BackupExpiryVerified);
            Assert.Empty(await restart.ExternalDataCaches.IgnoreQueryFilters().ToListAsync(Ct));
            Assert.NotNull((await restart.EvidenceCopyMarkers.SingleAsync(c => c.Id == committed.CopyId, Ct)).VerifiedRemovedAt);
        }
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => lifecycle.CommitAsync(delayed, Cache(delayed), Ct));
        await using var inspect = Demo(options);
        Assert.Empty(await inspect.ExternalDataCaches.IgnoreQueryFilters().ToListAsync(Ct));
        Assert.Equal(0, (await new EvidenceCopyLifecycle(inspect, clock).ReconcileAsync(Ct)).VerifiedRemoved);
    }

    [Fact]
    public async Task CompetingPreparedWritesCommitOnlyOnePayloadAndDoNotReturnAnUncommittedLoser()
    {
        var options = await postgres.CreateOptionsAsync(Ct);
        var clock = new CopyClock();
        await using var first = Demo(options);
        await using var second = Demo(options);
        var one = new EvidenceCopyLifecycle(first, clock);
        var two = new EvidenceCopyLifecycle(second, clock);
        var purpose = await one.OpenSyntheticPurposeAsync(ct: Ct);
        var a = await one.CaptureAsync(purpose, EvidenceCopyKind.MappedCache, "same-key", ct: Ct);
        var b = await two.CaptureAsync(purpose, EvidenceCopyKind.MappedCache, "same-key", ct: Ct);
        await one.CommitAsync(a, Cache(a), Ct);
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => two.CommitAsync(b, Cache(b), Ct));
        Assert.Empty(second.ChangeTracker.Entries());
        Assert.Single(await second.ExternalDataCaches.IgnoreQueryFilters().ToListAsync(Ct));
    }

    [Fact]
    public async Task ExpiryKeepsOriginalDeadlineAndInvalidatesConcreteDerivativeSources()
    {
        var options = await postgres.CreateOptionsAsync(Ct);
        var clock = new CopyClock();
        await using var db = Demo(options);
        var lifecycle = new EvidenceCopyLifecycle(db, clock);
        var purpose = await lifecycle.OpenSyntheticPurposeAsync(ct: Ct);
        var receipt = await lifecycle.CaptureAsync(purpose, EvidenceCopyKind.MappedCache, "source", ct: Ct);
        var source = await lifecycle.CommitAsync(receipt, Cache(receipt), Ct);
        var derivative = await lifecycle.CaptureAsync(purpose, EvidenceCopyKind.MappedCache, "derived", [source], Ct);
        await lifecycle.CommitAsync(derivative, Cache(derivative, TimeSpan.FromDays(10)), Ct);
        var pending = await lifecycle.CaptureAsync(purpose, EvidenceCopyKind.MappedCache, "delayed-derived", [source], Ct);
        clock.Advance(TimeSpan.FromHours(49));
        Assert.Equal(1, (await lifecycle.InspectAsync(Ct)).Overdue);
        Assert.Equal(2, (await lifecycle.ReconcileAsync(Ct)).VerifiedRemoved);
        Assert.Empty(await db.ExternalDataCaches.IgnoreQueryFilters().ToListAsync(Ct));
        var marker = await db.EvidenceCopyMarkers.SingleAsync(c => c.Id == source.CopyId, Ct);
        Assert.Equal(receipt.OriginalAcquiredAt.AddMinutes(1).AddHours(48), marker.RemovalDueAt);
        Assert.Equal(receipt.OriginalAcquiredAt.AddMinutes(1), marker.UnavailableAt);
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => lifecycle.CommitAsync(pending, Cache(pending), Ct));
    }

    [Fact]
    public async Task PurposeClosureRetryPreservesEarliestClockAndSevenDayCeiling()
    {
        var options = await postgres.CreateOptionsAsync(Ct);
        var clock = new CopyClock();
        await using var db = Demo(options);
        var lifecycle = new EvidenceCopyLifecycle(db, clock);
        var purpose = await lifecycle.OpenSyntheticPurposeAsync(ct: Ct);
        var receipt = await lifecycle.CaptureAsync(purpose, EvidenceCopyKind.MappedCache, "long-lived", ct: Ct);
        await lifecycle.CommitAsync(receipt, Cache(receipt, TimeSpan.FromDays(30)), Ct);
        var original = clock.GetUtcNow();
        await lifecycle.EndPurposeAsync(purpose, original, Ct);
        clock.Advance(TimeSpan.FromDays(2));
        await lifecycle.EndPurposeAsync(purpose, clock.GetUtcNow(), Ct);
        Assert.Equal(original, (await db.EvidencePurposes.SingleAsync(Ct)).OriginalEndedAt);
        Assert.Equal(original.AddDays(7), (await db.EvidenceCopyMarkers.SingleAsync(Ct)).RemovalDueAt);
        Assert.Equal(2, (await db.EvidencePurposes.SingleAsync(Ct)).Generation);
    }

    [Fact]
    public async Task RealAndUnknownCannotMintSyntheticPurposeOrAcquireUsingDemoReceipt()
    {
        var options = await postgres.CreateOptionsAsync(Ct);
        var clock = new CopyClock();
        await using var demo = Demo(options);
        var lifecycle = new EvidenceCopyLifecycle(demo, clock);
        var purpose = await lifecycle.OpenSyntheticPurposeAsync(ct: Ct);
        var receipt = await lifecycle.CaptureAsync(purpose, EvidenceCopyKind.MappedCache, "synthetic", ct: Ct);
        foreach (var provenance in new[] { DataProvenance.Real, DataProvenance.Unknown })
        {
            await using var other = new AppDbContext(options, new IRacingDataScope(provenance));
            var unavailable = new EvidenceCopyLifecycle(other, clock);
            await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => unavailable.OpenSyntheticPurposeAsync(ct: Ct));
            await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => unavailable.CommitAsync(receipt, Cache(receipt), Ct));
            await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => unavailable.EndPurposeAsync(purpose, clock.GetUtcNow(), Ct));
        }
    }

    private static AppDbContext Demo(DbContextOptions<AppDbContext> options) => new(options, new IRacingDataScope(DataProvenance.Demo));
    private static MappedCacheBatch Cache(EvidenceWriteReceipt receipt, TimeSpan? ttl = null) => new(new ExternalDataCache
    {
        CacheKey = receipt.KeyHash,
        Payload = "{\"synthetic\":true}",
        FetchedAt = receipt.OriginalAcquiredAt,
        ExpiresAt = receipt.OriginalAcquiredAt + (ttl ?? TimeSpan.FromMinutes(1)),
    });
    private sealed class CopyClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now += amount;
    }
}
