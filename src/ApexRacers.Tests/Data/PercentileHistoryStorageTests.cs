using System.Text.Json;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Core.PercentileHistory;
using ApexRacers.Data;
using ApexRacers.Data.Migrations;
using ApexRacers.Tests.Helpers;
using ApexRacers.Tests.Restore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace ApexRacers.Tests.Data;

[Collection(PostgreSqlCollection.Name)]
public sealed class PercentileHistoryStorageTests(PostgreSqlFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly Guid User = new("aaaaaaaa-3750-4000-8000-000000000001");
    private static readonly Guid Week = new("aaaaaaaa-3770-4000-8000-000000000010");
    private static readonly Guid LegacyRow = new("aaaaaaaa-4260-4000-8000-000000000001");
    private static readonly DateTimeOffset Original = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Actual_snapshot_upgrade_preserves_unclassified_history_and_fences_prepared_old_SQL()
    {
        await using var db = await CreateHistoricalAsync();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261009162419_SyntheticDriverOperatingControls", Ct);
        var connectionString = db.Database.GetConnectionString()!;
        var snapshot = await PrimarySnapshot.CaptureAsync(fixture, connectionString, Ct);
        await using (var old = new NpgsqlConnection(connectionString))
        {
            await old.OpenAsync(Ct);
            var texts = new[]
            {
                "SELECT COUNT(*) FROM iracing.\"ScopedCarPercentileResults\"",
                "INSERT INTO iracing.\"ScopedCarPercentileResults\" DEFAULT VALUES",
                "UPDATE iracing.\"ScopedCarPercentileResults\" SET \"PercentileRank\" = 52"
            };
            var prepared = texts.Select(sql => new NpgsqlCommand(sql, old)).ToArray();
            try
            {
                foreach (var command in prepared) await command.PrepareAsync(Ct);
                Assert.Equal(1L, await prepared[0].ExecuteScalarAsync(Ct));
                await migrator.MigrateAsync(cancellationToken: Ct);
                foreach (var command in prepared)
                {
                    var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteScalarAsync(Ct));
                    Assert.Equal(PostgresErrorCodes.UndefinedTable, error.SqlState);
                }
                foreach (var sql in texts)
                {
                    await using var fresh = new NpgsqlCommand(sql, old);
                    var error = await Assert.ThrowsAsync<PostgresException>(() => fresh.ExecuteScalarAsync(Ct));
                    Assert.Equal(PostgresErrorCodes.UndefinedTable, error.SqlState);
                }
            }
            finally { foreach (var command in prepared) await command.DisposeAsync(); }
        }
        await AssertLegacyPreservedAsync(db);
        Assert.Empty(await db.ClassifiedCarPercentileResults.IgnoreQueryFilters().ToListAsync(Ct));
        Assert.Throws<NotSupportedException>(() => new ClassifyPercentileHistory().DownOperations);
        await Assert.ThrowsAsync<NotSupportedException>(() => migrator.MigrateAsync("20261009162419_SyntheticDriverOperatingControls", Ct));

        // A restored old schema has no current fence until actual migrations are applied again.
        await snapshot.RestoreAsync(fixture, connectionString, Ct, replaceSchemas: true);
        await migrator.MigrateAsync(cancellationToken: Ct);
        await AssertLegacyPreservedAsync(db);
        Assert.Empty(await db.ClassifiedCarPercentileResults.IgnoreQueryFilters().ToListAsync(Ct));
        await snapshot.RecordAsync("percentile-history-cutover", new
        {
            LegacyId = LegacyRow, OriginalComputedAt = Original,
            PreservedLegacyRows = 1, ClassifiedRows = 0,
            PreparedAndFreshReaderInsertUpdate = PostgresErrorCodes.UndefinedTable,
            Rollback = "refused; restore activation requires current migrations and independent enforcement",
            CutoverMigration = "20261015011526_ClassifyPercentileHistory"
        }, true);
    }

    [Fact]
    public async Task Classified_storage_separates_selections_enforces_identity_and_preserves_copy_clocks()
    {
        await using var db = await CreateHistoricalAsync();
        await db.Database.MigrateAsync(Ct);
        var purpose = new EvidencePurpose { Id = Guid.NewGuid(), Provenance = DataProvenance.Demo,
            Kind = EvidencePurposeKind.SyntheticPreview, CreatedAt = Original };
        db.EvidencePurposes.Add(purpose);
        await db.SaveChangesAsync(Ct);
        var race = PercentileSelection.FromRequest(false, null);
        var unrestricted = PercentileSelection.FromRequest(true, null);
        var first = await InsertAsync(db, purpose, race, 1);
        var second = await InsertAsync(db, purpose, unrestricted, 1);
        Assert.Equal(2, await db.ClassifiedCarPercentileResults.CountAsync(Ct));
        Assert.Single(await db.CarPercentileResults.IgnoreQueryFilters().ToListAsync(Ct));
        Assert.All(await db.EvidenceCopyMarkers.ToListAsync(Ct), m => Assert.Equal(Original, m.OriginalAcquiredAt));
        Assert.All(await db.ClassifiedCarPercentileResults.ToListAsync(Ct), r => Assert.Equal(0, r.PreparationVersion));

        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => InsertAsync(db, purpose, race, 2));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(duplicate.InnerException).SqlState);
        db.ChangeTracker.Clear();
        var forged = await Assert.ThrowsAsync<DbUpdateException>(() => InsertAsync(db, purpose, race, 3,
            row => row.UserId = Guid.NewGuid()));
        Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(forged.InnerException).SqlState);
        db.ChangeTracker.Clear();
        var unknownPolicy = await Assert.ThrowsAsync<DbUpdateException>(() => InsertAsync(db, purpose, race, 4,
            row => row.SelectionPolicyVersion = 2));
        Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(unknownPolicy.InnerException).SqlState);
        db.ChangeTracker.Clear();
        var forgedPersonal = await Assert.ThrowsAsync<DbUpdateException>(() => InsertAsync(db, purpose, race, 5,
            row => { row.OwnerKind = PercentileOwnerKind.Personal; row.GrantId = Guid.NewGuid(); row.GrantRevision = 1; }));
        Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(forgedPersonal.InnerException).SqlState);
        db.ChangeTracker.Clear();
        foreach (var mutate in new Action<ClassifiedCarPercentileResult>[]
        {
            row => row.PersonalBestLapSeconds = double.NaN,
            row => row.PersonalBestLapSeconds = double.PositiveInfinity,
            row => { row.PersonalBestLapEvidence = LapEvidence.UploadedLap; row.UploadedSessionType = null; },
            row => { row.OwnerKind = PercentileOwnerKind.Personal; row.GrantId = Guid.NewGuid(); row.GrantRevision = null; },
            row => row.PreparationVersion = -1,
            row => row.PreparationGeneration = 2,
            row => row.ReadingRevision = 999
        })
        {
            var invalid = await Assert.ThrowsAsync<DbUpdateException>(() => InsertAsync(db, purpose, race, 6, mutate));
            Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(invalid.InnerException).SqlState);
            db.ChangeTracker.Clear();
        }
        Assert.Equal(2, await db.ClassifiedCarPercentileResults.CountAsync(Ct));
        Assert.Equal(new[] { first.Id, second.Id }.Order(), (await db.ClassifiedCarPercentileResults.Select(r => r.Id).ToArrayAsync(Ct)).Order());

        // Measure actual planner choices without forcing index scans or adding speculative indexes.
        var plans = new Dictionary<string, string>();
        await db.Database.OpenConnectionAsync(Ct);
        foreach (var (label, sql) in new[]
        {
            ("owner-selection-series", "EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) SELECT * FROM iracing.\"SelectionScopedCarPercentileResults\" WHERE \"Provenance\"=2 AND \"UserId\"=@user AND \"OwnerKind\"=0 AND \"CustomerId\"=100001 AND \"SelectionKey\"='v1:0:0' AND \"SeriesId\"=10"),
            ("week-refresh", "EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) SELECT * FROM iracing.\"SelectionScopedCarPercentileResults\" WHERE \"Provenance\"=2 AND \"UserId\"=@user AND \"OwnerKind\"=0 AND \"CustomerId\"=100001 AND \"SelectionKey\"='v1:0:0' AND \"SeriesId\"=10 AND \"CarId\"=40 AND \"WeekId\"=@week")
        })
        {
            await using var command = new NpgsqlCommand(sql, (NpgsqlConnection)db.Database.GetDbConnection());
            command.Parameters.AddWithValue("user", User); command.Parameters.AddWithValue("week", Week);
            plans[label] = (string)(await command.ExecuteScalarAsync(Ct))!;
            using var parsed = JsonDocument.Parse(plans[label]);
            Assert.True(parsed.RootElement[0].GetProperty("Plan").GetProperty("Actual Rows").GetDouble() > 0);
        }
        var output = PrivacyContract.CatalogAdmission.OutputDirectory("percentile-history");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "storage-plans.json"), JsonSerializer.Serialize(new
        {
            PostgreSqlImage = "postgres:18.0-alpine", Rows = 2, Plans = plans,
            Scope = "Small controlled synthetic fixture, actual planner choices; no scale-performance claim or extra query index."
        }, new JsonSerializerOptions { WriteIndented = true }), Ct);
    }

    private static async Task<ClassifiedCarPercentileResult> InsertAsync(AppDbContext db, EvidencePurpose purpose,
        PercentileSelection selection, long version, Action<ClassifiedCarPercentileResult>? mutate = null)
    {
        var owner = new PercentileOwnerIdentity(User, 100001, DataProvenance.Demo, PercentileOwnerKind.DemoSubject);
        var marker = new EvidenceCopyMarker { Id = Guid.NewGuid(), PurposeId = purpose.Id, Provenance = DataProvenance.Demo,
            Kind = EvidenceCopyKind.PersonalDerivative, KeyHash = PercentileHistoryKey.Hash(PercentileHistoryKey.Create(owner, null, selection, 40, 10, Week)),
            Generation = 1, Version = version, OriginalAcquiredAt = Original };
        var row = new ClassifiedCarPercentileResult { Id = Guid.NewGuid(), UserId = User, CustomerId = 100001,
            Provenance = DataProvenance.Demo, OwnerKind = PercentileOwnerKind.DemoSubject, SelectionPolicyVersion = 1,
            SelectionKey = selection.Key, CarId = 40, SeriesId = 10, WeekId = Week, PercentileRank = 50,
            TopSharePercent = 50, SampleSize = 5, ComputedAt = Original, PersonalBestLapSeconds = 90,
            PersonalBestLapEvidence = LapEvidence.RaceLap, ReadingRevision = version, EvidenceCopyId = marker.Id,
            PreparationPurposeId = purpose.Id, PreparationGeneration = 1, PreparationVersion = 0 };
        mutate?.Invoke(row);
        await using var tx = await db.Database.BeginTransactionAsync(Ct);
        db.EvidenceCopyMarkers.Add(marker); db.ClassifiedCarPercentileResults.Add(row);
        await db.SaveChangesAsync(Ct);
        await tx.CommitAsync(Ct);
        return row;
    }

    private static async Task AssertLegacyPreservedAsync(AppDbContext db)
    {
        db.ChangeTracker.Clear();
        var row = Assert.Single(await db.CarPercentileResults.IgnoreQueryFilters().ToListAsync(Ct));
        Assert.Equal(LegacyRow, row.Id); Assert.Equal(Original, row.ComputedAt);
        Assert.Equal(50, row.PercentileRank); Assert.Equal(5, row.SampleSize);
        Assert.Equal(DataProvenance.Unknown, row.Provenance); Assert.Null(row.EvidenceCopyId);
    }

    private async Task<AppDbContext> CreateHistoricalAsync()
    {
        var database = "apexracers_restore_legacy_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(fixture.Container.GetConnectionString());
        await admin.OpenAsync(Ct);
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin)) await create.ExecuteNonQueryAsync(Ct);
        var connection = new NpgsqlConnectionStringBuilder(fixture.Container.GetConnectionString()) { Database = database,
            Host = "127.0.0.1", Pooling = false }.ConnectionString;
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection,
            o => o.MigrationsHistoryTable("__EFMigrationsHistory", "iracing")).Options, new IRacingDataScope(DataProvenance.Demo));
        await db.GetService<IMigrator>().MigrateAsync("20260912044021_AddKnownDevices", Ct);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO identity."Users" ("Id", "DisplayName", "IRacingCustomerId", "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
            VALUES ({0}, 'Synthetic history User', 100001, true, false, false, false, 0);
            INSERT INTO iracing."Cars" ("Id", "Name", "NameAbbreviated", "RainEnabled") VALUES (40, 'Synthetic history car', 'SHC', false);
            INSERT INTO iracing."Tracks" ("Id", "Name", "ConfigName", "IsDirt", "IsOval", "Retired", "NightLighting", "HasSvgMap")
            VALUES (30, 'Synthetic history track', '', false, false, false, false, false);
            INSERT INTO iracing."Series" ("Id", "Name") VALUES (10, 'Synthetic history Series');
            INSERT INTO iracing."Seasons" ("Id", "SeriesId", "Year", "Quarter", "Active") VALUES (20, 10, 2020, 1, true);
            INSERT INTO iracing."Weeks" ("Id", "SeasonId", "RaceWeekIndex", "TrackId", "StartDate") VALUES ({1}, 20, 0, 30, DATE '2020-01-01');
            INSERT INTO iracing."CarPercentileResults" ("Id", "UserId", "CarId", "SeriesId", "WeekId", "PercentileRank", "TopSharePercent", "SampleSize", "ComputedAt")
            VALUES ({2}, {0}, 40, 10, {1}, 50, 50, 5, {3});
            """, new object[] { User, Week, LegacyRow, Original }, Ct);
        return db;
    }
}
