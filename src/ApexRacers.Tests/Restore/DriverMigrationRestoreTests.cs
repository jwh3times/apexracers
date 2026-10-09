using System.Net;
using System.Net.Http.Headers;
using ApexRacers.Core;
using ApexRacers.Data;
using ApexRacers.Data.Migrations;
using ApexRacers.Tests.Helpers;
using ApexRacers.Tests.References;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace ApexRacers.Tests.Restore;

[Collection(PostgreSqlCollection.Name)]
public sealed class DriverMigrationRestoreTests(PostgreSqlFixture fixture) : IAsyncLifetime
{
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;
    public ValueTask DisposeAsync() => LegacyApiProcess.CleanupBuildAsync();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    [Fact]
    public async Task Actual_legacy_binary_cannot_read_uploaded_laps_after_the_real_upgrade_chain()
    {
        var database = "apexracers_restore_legacy_" + Guid.NewGuid().ToString("N");
        await using (var connection = new NpgsqlConnection(fixture.Container.GetConnectionString()))
        {
            await connection.OpenAsync(Ct);
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", connection);
            await create.ExecuteNonQueryAsync(Ct);
        }
        var target = new NpgsqlConnectionStringBuilder(fixture.Container.GetConnectionString()) { Database = database, Host = "127.0.0.1", Pooling = false }.ConnectionString;
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(target, o => o.MigrationsHistoryTable("__EFMigrationsHistory", "iracing")).Options, new IRacingDataScope(DataProvenance.Real));
        var migration = db.GetService<IMigrator>();
        await migration.MigrateAsync("20260912044021_AddKnownDevices", Ct);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO identity."Users" ("Id", "DisplayName", "IRacingCustomerId", "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
            VALUES ('aaaaaaaa-3750-4000-8000-000000000001', 'Synthetic unverified legacy User', 100001, true, false, false, false, 0);
            INSERT INTO iracing."Cars" ("Id", "Name", "NameAbbreviated", "RainEnabled") VALUES (40, 'Synthetic legacy car', 'SLC', false);
            INSERT INTO iracing."Tracks" ("Id", "Name", "ConfigName", "IsDirt", "IsOval", "Retired", "NightLighting", "HasSvgMap")
            VALUES (30, 'Synthetic legacy track', '', false, false, false, false, false);
            INSERT INTO iracing."UploadedLaps" ("Id", "UserId", "DriverCustId", "CarId", "TrackId", "LapTimeSeconds", "SessionType", "AirTempCelsius", "TrackTempCelsius", "TrackWetness", "RecordedAt")
            VALUES ('aaaaaaaa-3770-4000-8000-000000000001', 'aaaaaaaa-3750-4000-8000-000000000001', 100001, 40, 30, 93.125, 0, 20, 30, 0, TIMESTAMPTZ '2020-01-01 00:00:00+00');
            """, Ct);
        await LegacyRestoreDataset.SeedAdditionalAsync(db, Ct);
        var snapshot = await PrimarySnapshot.CaptureAsync(fixture, target, Ct);
        // Exercise actual restore of the legacy schema, not just a fresh database upgrade.
        await snapshot.RestoreAsync(fixture, target, Ct, replaceSchemas: true);
        await using var mixed = await LegacyApiProcess.StartAsync(target, Ct);
        using (var permissive = await ReadAsync(mixed, "/api/telemetry/laps"))
        {
            Assert.Equal(HttpStatusCode.OK, permissive.StatusCode);
            Assert.Contains("93.125", await permissive.Content.ReadAsStringAsync(Ct));
        }
        await migration.MigrateAsync(cancellationToken: Ct);
        await using var legacy = await LegacyApiProcess.StartAsync(target, Ct);
        using var response = await ReadAsync(legacy, "/api/telemetry/laps");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("93.125", body);
        Assert.DoesNotContain("Synthetic legacy car", body);
        Assert.Empty(await db.DriverAuthorizationGrants.ToListAsync(Ct));
        Assert.Empty(await db.DriverProofReceipts.ToListAsync(Ct));
        using (var oldRunning = await ReadAsync(mixed, "/api/telemetry/laps")) Assert.Equal(HttpStatusCode.InternalServerError, oldRunning.StatusCode);
        Assert.All(await db.UploadedLaps.ToListAsync(Ct), row => Assert.Equal(LegacyRestoreDataset.Original, row.RecordedAt));
        Assert.Equal(3, await db.UploadedLaps.CountAsync(Ct));
        Assert.Equal(100001L, (await db.Users.SingleAsync(Ct)).IRacingCustomerId);
        Assert.Equal(2, await db.QuarantinedDataCaches.CountAsync(Ct));
        Assert.All(await db.QuarantinedDataCaches.ToListAsync(Ct), row => Assert.Equal(LegacyRestoreDataset.Original, row.FetchedAt));
        var originalProfile = await db.QuarantinedDataCaches.SingleAsync(c => c.CacheKey == "profile:100001", Ct);
        Assert.Equal(new DateTimeOffset(9999, 1, 1, 0, 0, 0, TimeSpan.Zero), originalProfile.ExpiresAt);
        Assert.Equal("{\"DriverName\":\"Synthetic unknown legacy profile\"}", originalProfile.Payload);
        Assert.Equal(LegacyRestoreDataset.Original.AddDays(1), (await db.QuarantinedDataCaches.SingleAsync(c => c.CacheKey == "profile:100002", Ct)).ExpiresAt);
        Assert.Equal(new long?[] { 100001, null, 100002 }, await db.UploadedLaps.OrderBy(l => l.Id).Select(l => l.DriverCustId).ToArrayAsync(Ct));
        Assert.Empty(await db.EvidencePurposes.ToListAsync(Ct));
        Assert.Empty(await db.EvidenceCopyMarkers.ToListAsync(Ct));
        Assert.Empty(await db.DriverCopyCleanups.ToListAsync(Ct));
        Assert.All(await db.Subsessions.IgnoreQueryFilters().ToListAsync(Ct), s => { Assert.Equal(DataProvenance.Unknown, s.Provenance); Assert.Null(s.EvidenceCopyId); Assert.Equal(1, s.TeamEntryCount); Assert.Equal(1, s.AiEntryCount); });
        Assert.Equal(new long[] { 100001, 100002 }, await db.SubsessionResults.IgnoreQueryFilters().OrderBy(r => r.CustId).Select(r => r.CustId).ToArrayAsync(Ct));
        var inventory = await db.ProvenanceMigrationInventory.OrderBy(i => i.StorageKind).ToArrayAsync(Ct);
        Assert.Equal(new Dictionary<string, long> { ["mapped-cache"] = 2, ["subsession"] = 1, ["race-result"] = 2, ["percentile"] = 1, ["follow"] = 1, ["bop"] = 1, ["weather"] = 1, ["uploaded-lap"] = 3 }, inventory.ToDictionary(i => i.StorageKind, i => i.UnknownRows));
        var oldWrite = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM iracing.\"UploadedLaps\"", Ct));
        Assert.Equal(PostgresErrorCodes.UndefinedTable, oldWrite.SqlState);
        Assert.Throws<NotSupportedException>(() => new FenceLegacyUploadedLapReaders().DownOperations);
        await Assert.ThrowsAsync<NotSupportedException>(() => migration.MigrateAsync("20261008132131_ScopedDriverReferences", Ct));
        // Observe quarantine before ordinary startup's prompt unclassified-data erasure runs.
        await using var current = await LegacyApiProcess.StartAsync(target, Ct, current: true);
        using (var closed = await ReadAsync(current, "/api/telemetry/laps"))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, closed.StatusCode);
            Assert.Equal("no-store", closed.Headers.CacheControl?.ToString());
        }
        using (var catalog = await ReadAsync(current, "/api/cars"))
        {
            Assert.Equal(HttpStatusCode.OK, catalog.StatusCode);
            Assert.Contains("Synthetic legacy car", await catalog.Content.ReadAsStringAsync(Ct));
        }
        await snapshot.RecordAsync("legacy-upgrade-mixed-rollback", new
        {
            LegacyBinarySha256 = await LegacyApiProcess.BinarySha256Async(Ct),
            Hosts = new[] { mixed.ProcessId, legacy.ProcessId, current.ProcessId },
            Inventory = inventory.Select(i => new { i.StorageKind, i.UnknownRows }),
            LegacyBeforeUpgrade = 200,
            MixedAndRolledBackBinary = 500,
            CurrentDenied = 503,
            IndependentCatalog = 200,
            Topology = "Restored pre-provenance PostgreSQL snapshot; actual pinned old API stays running across full upgrade; fresh old/current API processes"
        }, true);
    }

    private static async Task<HttpResponseMessage> ReadAsync(LegacyApiProcess host, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ReferenceActors.Token(ReferenceActors.Recipient));
        return await host.Client.SendAsync(request, Ct);
    }
}
