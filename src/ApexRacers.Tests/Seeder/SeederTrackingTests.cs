using System.Globalization;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Seeder.Demo;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace ApexRacers.Tests.Seeder;

[Collection(PostgreSqlCollection.Name)]
public sealed class SeederTrackingTests(PostgreSqlFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CompletedRaceGraphIsNotRescannedByFirstMappedCacheSaveAndRerunPreservesSources(bool automatic)
    {
        var probe = new CacheSaveProbe();
        await using var db = await CreateAsync(probe);
        var source = await GraphAsync(db);
        Assert.Equal(5, db.ChangeTracker.Entries<SubsessionResult>().Count());
        db.ChangeTracker.AutoDetectChangesEnabled = automatic;
        probe.Observe = true;
        await new DemoCacheSeeder(db).SeedAllAsync(Ct);
        Assert.Equal(automatic, db.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(0, probe.FirstSaveResults);
        var mappedCount = await db.ExternalDataCaches.CountAsync(Ct);
        Assert.True(mappedCount > 0);
        // A normal caller performs the full cache-upsert rerun after flag preservation was checked.
        db.ChangeTracker.AutoDetectChangesEnabled = true;
        await new DemoCacheSeeder(db).SeedAllAsync(Ct);
        Assert.Equal(mappedCount, await db.ExternalDataCaches.CountAsync(Ct));
        Assert.Equal(1, await db.Subsessions.CountAsync(Ct));
        Assert.Equal(5, await db.SubsessionResults.CountAsync(Ct));
        var retained = await db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(m => m.Id == source.Id, Ct);
        Assert.Equal(source.OriginalAcquiredAt, retained.OriginalAcquiredAt);
        Assert.Null(retained.UnavailableAt);
        Assert.Null(retained.VerifiedRemovedAt);
    }

    [Theory]
    [InlineData(EntityState.Added, false)]
    [InlineData(EntityState.Modified, false)]
    [InlineData(EntityState.Modified, true)]
    [InlineData(EntityState.Deleted, false)]
    public async Task CachePhaseDoesNotDiscardPendingCatalogWrites(EntityState state, bool implicitChange)
    {
        await using var db = await CreateAsync();
        await GraphAsync(db);
        var car = new Car { Id = 999, Name = "Pending fixture car", NameAbbreviated = "PF" };
        if (state != EntityState.Added)
        {
            db.Cars.Add(car);
            await db.SaveChangesAsync(Ct);
        }
        if (state == EntityState.Added) db.Cars.Add(car);
        else if (state == EntityState.Deleted) db.Cars.Remove(car);
        else { car.Name = "Pending changed name"; if (!implicitChange) db.Entry(car).State = EntityState.Modified; }
        // The caller's DetectChanges setting and implicit or already-marked pending work remain.
        // Clearing only currently-marked states would lose the implicit-change case.
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        await new DemoCacheSeeder(db).SeedAllAsync(Ct);
        Assert.False(db.ChangeTracker.AutoDetectChangesEnabled);
        var stored = await db.Cars.AsNoTracking().SingleOrDefaultAsync(c => c.Id == 999, Ct);
        if (state == EntityState.Deleted) Assert.Null(stored);
        else Assert.Equal(state == EntityState.Modified ? "Pending changed name" : "Pending fixture car", stored!.Name);
    }

    [Fact]
    public async Task PendingPartialFieldWriteStillFailsClosedInsteadOfBeingDiscardedByCachePreparation()
    {
        await using var db = await CreateAsync();
        var source = await GraphAsync(db);
        var lap = await db.SubsessionResults.SingleAsync(r => r.CustId == DemoData.DriverCustId, Ct);
        lap.BestLapSeconds = 1;
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => new DemoCacheSeeder(db).SeedAllAsync(Ct));
        Assert.False(db.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal(90, await db.SubsessionResults.Where(r => r.CustId == DemoData.DriverCustId).Select(r => r.BestLapSeconds).SingleAsync(Ct));
        Assert.Empty(await db.ExternalDataCaches.ToListAsync(Ct));
        Assert.Null((await db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(m => m.Id == source.Id, Ct)).UnavailableAt);
    }

    [Fact]
    public async Task CachePhaseAndRerunPreserveLegacyRealUnknownAndQuarantinedRows()
    {
        var options = await postgres.CreateOptionsAsync(Ct);
        await using var db = new AppDbContext(options, new IRacingDataScope(DataProvenance.Demo));
        await db.Database.EnsureDeletedAsync(Ct);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260912044021_AddKnownDevices", Ct);
        db.Series.Add(new() { Id = 10, Name = "Legacy preservation fixture" });
        db.Seasons.Add(new() { Id = 20, SeriesId = 10, Year = 2020, Quarter = 1 });
        db.Tracks.Add(new() { Id = 30, Name = "Legacy preservation Track" });
        db.Cars.Add(new() { Id = 40, Name = "Legacy preservation Car", NameAbbreviated = "LP" });
        db.CarClasses.Add(new() { Id = 50, Name = "Legacy preservation Class", ShortName = "LP" });
        await db.SaveChangesAsync(Ct);
        var original = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await InsertHistoricalAsync(db, "Subsessions", new Subsession
        { Id = -70, SeasonId = 20, TrackId = 30, StartTime = original }, beforeProvenance: true);
        await InsertHistoricalAsync(db, "SubsessionResults", new SubsessionResult
        { SubsessionId = -70, CustId = 700, CarId = 40, CarClassId = 50, BestLapSeconds = 123 }, beforeProvenance: true);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO iracing."ExternalDataCaches" ("CacheKey", "Payload", "FetchedAt", "ExpiresAt")
            VALUES ('fixture:unknown', {0}, TIMESTAMPTZ '2020-01-01 00:00:00+00', TIMESTAMPTZ '9999-01-01 00:00:00+00')
            """, new object[] { "{}" }, Ct);
        await migrator.MigrateAsync("20261003030330_IsolateDemoProvenance", Ct);
        // Historical Real rows precede the copy fences; no synthetic ownership or copy receipt
        // is invented. Forward migration retains them outside the Demo read/write namespace.
        await InsertHistoricalAsync(db, "RaceEvidenceSubsessions", new Subsession
        { Provenance = DataProvenance.Real, Id = -71, SeasonId = 20, TrackId = 30, StartTime = original });
        await InsertHistoricalAsync(db, "RaceEvidenceResults", new SubsessionResult
        { Provenance = DataProvenance.Real, SubsessionId = -71, CustId = 701, CarId = 40, CarClassId = 50, BestLapSeconds = 124 });
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO iracing."MappedDataCaches" ("Provenance", "CacheKey", "Payload", "FetchedAt", "ExpiresAt")
            VALUES (1, 'fixture:real', {0}, TIMESTAMPTZ '2020-01-01 00:00:00+00', TIMESTAMPTZ '9999-01-01 00:00:00+00')
            """, new object[] { "{}" }, Ct);
        await migrator.MigrateAsync(cancellationToken: Ct);
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "legacy-fixture", DisplayName = "Legacy fixture User", EmailConfirmed = true };
        db.Users.Add(user);
        db.UploadedLaps.Add(new()
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            DriverCustId = 700,
            CarId = 40,
            TrackId = 30,
            RecordedAt = original,
            LapTimeSeconds = 122
        });
        await db.SaveChangesAsync(Ct);
        await GraphAsync(db);
        var before = await PreservationHashAsync(db);
        Assert.Equal(2, await db.Subsessions.IgnoreQueryFilters().CountAsync(r => r.Provenance != DataProvenance.Demo, Ct));
        Assert.Equal(2, await db.SubsessionResults.IgnoreQueryFilters().CountAsync(r => r.Provenance != DataProvenance.Demo, Ct));
        Assert.Single(await db.QuarantinedDataCaches.ToListAsync(Ct));
        Assert.Single(await db.UploadedLaps.IgnoreQueryFilters().ToListAsync(Ct));
        await new DemoCacheSeeder(db).SeedAllAsync(Ct);
        Assert.Equal(before, await PreservationHashAsync(db));
        await new DemoCacheSeeder(db).SeedAllAsync(Ct);
        Assert.Equal(before, await PreservationHashAsync(db));
    }

    private static Task<string> PreservationHashAsync(AppDbContext db) => db.Database.SqlQueryRaw<string>("""
        SELECT md5(string_agg(payload, E'\n' ORDER BY payload)) AS "Value" FROM (
            SELECT 'races:' || row_to_json(t)::text AS payload FROM iracing."RaceEvidenceSubsessions" t WHERE "Provenance" <> 2
            UNION ALL SELECT 'results:' || row_to_json(t)::text FROM iracing."RaceEvidenceResults" t WHERE "Provenance" <> 2
            UNION ALL SELECT 'cache:' || row_to_json(t)::text FROM iracing."MappedDataCaches" t WHERE "Provenance" <> 2
            UNION ALL SELECT 'quarantined-cache:' || row_to_json(t)::text FROM iracing."QuarantinedDataCaches" t
            UNION ALL SELECT 'quarantined-upload:' || row_to_json(t)::text FROM iracing."QuarantinedUploadedLaps" t
        ) retained
        """).SingleAsync(Ct);

    private static Task InsertHistoricalAsync<T>(AppDbContext db, string table, T value, bool beforeProvenance = false)
        where T : class
    {
        var properties = db.Model.FindEntityType(typeof(T))!.GetProperties()
            .Where(p => p.Name != nameof(IManagedEvidence.EvidenceCopyId)
                && (!beforeProvenance || p.Name != nameof(IProvenancedData.Provenance))
                && p.PropertyInfo!.GetValue(value) is not null).ToArray();
        var columns = string.Join(", ", properties.Select(p => $"\"{p.GetColumnName()}\""));
        var placeholders = string.Join(", ", Enumerable.Range(0, properties.Length)
            .Select(i => "{" + i.ToString(CultureInfo.InvariantCulture) + "}"));
        return db.Database.ExecuteSqlRawAsync($"INSERT INTO iracing.\"{table}\" ({columns}) VALUES ({placeholders})",
            properties.Select(p => p.PropertyInfo!.GetValue(value)!).ToArray(), Ct);
    }

    private async Task<AppDbContext> CreateAsync(params IInterceptor[] interceptors)
    {
        var options = await postgres.CreateOptionsAsync(Ct);
        await using (var setup = new AppDbContext(options))
        { await setup.Database.EnsureDeletedAsync(Ct); await setup.Database.MigrateAsync(Ct); }
        return new(postgres.WithInterceptors(options, interceptors), new IRacingDataScope(DataProvenance.Demo));
    }

    private static async Task<EvidenceCopyMarker> GraphAsync(AppDbContext db)
    {
        db.Series.Add(new() { Id = 444, Name = "Synthetic tracker Series" });
        db.Seasons.Add(new() { Id = 6115, SeriesId = 444, Active = true, Year = 2026, Quarter = 2 });
        db.Cars.Add(new() { Id = 132, Name = "Synthetic tracker Car", NameAbbreviated = "ST" });
        db.CarClasses.Add(new() { Id = 100, Name = "Synthetic tracker Class", ShortName = "ST" });
        db.CarClassCars.Add(new() { CarId = 132, CarClassId = 100 });
        db.SeasonCars.Add(new() { SeasonId = 6115, CarId = 132 });
        db.SeasonCarClasses.Add(new() { SeasonId = 6115, CarClassId = 100 });
        db.Tracks.Add(new() { Id = 1, Name = "Synthetic tracker Track" });
        var week = Guid.NewGuid();
        db.Weeks.Add(new() { Id = week, SeasonId = 6115, TrackId = 1, StartDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1) });
        var writer = await SyntheticEvidenceWriter.OpenAsync(db, Ct);
        await writer.SaveChangesAsync(Ct);
        db.Subsessions.Add(new() { Id = -10, SeasonId = 6115, TrackId = 1, WeekId = week, OfficialSession = true, StartTime = DateTimeOffset.UtcNow });
        db.SubsessionResults.AddRange(Enumerable.Range(0, 5).Select(index => new SubsessionResult
        {
            SubsessionId = -10,
            CustId = DemoData.DriverCustId + index,
            CarId = 132,
            CarClassId = 100,
            BestLapSeconds = 90 + index,
            FinishPosition = index,
            FinishPositionInClass = index,
        }));
        await writer.SaveChangesAsync(Ct);
        // Prepare existing weather/BoP through the normal typed writer before tests alter the
        // caller's detection setting; AutoDetect=false is tested at the cache phase boundary.
        await new DemoCacheSeeder(db).SeedBopAndWeatherAsync(Ct);
        return await db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(m => m.Kind == EvidenceCopyKind.OfficialField, Ct);
    }

    private sealed class CacheSaveProbe : SaveChangesInterceptor
    {
        public bool Observe { get; set; }
        public int? FirstSaveResults { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Observe && FirstSaveResults is null)
            {
                var tracker = data.Context!.ChangeTracker;
                var automatic = tracker.AutoDetectChangesEnabled;
                try
                {
                    tracker.AutoDetectChangesEnabled = false;
                    if (tracker.Entries<ExternalDataCache>().Any(e => e.State is EntityState.Added or EntityState.Modified))
                        FirstSaveResults = tracker.Entries<SubsessionResult>().Count();
                }
                finally { tracker.AutoDetectChangesEnabled = automatic; }
            }
            return ValueTask.FromResult(result);
        }
    }
}
