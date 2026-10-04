using System.Globalization;
using ApexRacers.Api.Services;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Data.Migrations;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace ApexRacers.Tests.Data;

[Collection(PostgreSqlCollection.Name)]
public class DemoProvenanceMigrationTests(PostgreSqlFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task FullMigrationChainQuarantinesAmbiguousRowsAndFencesOldBinaries()
    {
        await using var db = await postgres.CreateDbContextAsync(Ct);
        await db.Database.EnsureDeletedAsync(Ct);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260912044021_AddKnownDevices", Ct);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO iracing."ExternalDataCaches" ("CacheKey", "Payload", "FetchedAt", "ExpiresAt")
            VALUES ('profile:100001', {0},
                TIMESTAMPTZ '2020-01-01 00:00:00+00', TIMESTAMPTZ '9999-01-01 00:00:00+00'),
                ('profile:200', {1},
                TIMESTAMPTZ '2020-01-01 00:00:00+00', TIMESTAMPTZ '2020-01-02 00:00:00+00');
            """, new object[] {
                "{\"DisplayName\":\"Legacy possibly synthetic name\"}",
                "{\"DisplayName\":\"Legacy other name\"}" }, Ct);
        db.Series.Add(new Series { Id = 10, Name = "Synthetic fixture series" });
        db.Seasons.Add(new Season { Id = 20, SeriesId = 10 });
        db.Tracks.Add(new Track { Id = 30, Name = "Synthetic fixture track" });
        await db.SaveChangesAsync(Ct);
        await InsertLegacySubsessionAsync(db, new Subsession
        {
            Id = -7, SeasonId = 20, TrackId = 30,
            StartTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
        });

        await migrator.MigrateAsync(cancellationToken: Ct);
        Assert.Empty(await db.ExternalDataCaches.ToListAsync(Ct));
        Assert.Empty(await db.Subsessions.ToListAsync(Ct));
        Assert.Equal(2, await db.QuarantinedDataCaches.CountAsync(Ct));
        Assert.Equal(2, (await db.ProvenanceMigrationInventory.SingleAsync(i => i.StorageKind == "mapped-cache", Ct)).UnknownRows);
        Assert.Equal(1, (await db.ProvenanceMigrationInventory.SingleAsync(i => i.StorageKind == "subsession", Ct)).UnknownRows);
        var preserved = await db.QuarantinedDataCaches.SingleAsync(c => c.CacheKey == "profile:100001", Ct);
        Assert.Equal(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), preserved.FetchedAt);
        Assert.Equal("Legacy possibly synthetic name", System.Text.Json.JsonDocument.Parse(preserved.Payload).RootElement.GetProperty("DisplayName").GetString());
        Assert.Equal(DataProvenance.Unknown, (await db.Subsessions.IgnoreQueryFilters().SingleAsync(Ct)).Provenance);
        await Assert.ThrowsAsync<IRacingNotConfiguredException>(() => new CachedIRacingClient(db, null)
            .GetOrFetchAsync(IRacingCacheKeys.Profile(100001), _ => Task.FromResult(17), Ct));

        var oldWrite = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE iracing.\"Subsessions\" SET \"OfficialSession\" = true", Ct));
        Assert.Equal(PostgresErrorCodes.UndefinedTable, oldWrite.SqlState);
        var oldCache = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "DELETE FROM iracing.\"ExternalDataCaches\"", Ct));
        Assert.Equal(PostgresErrorCodes.UndefinedTable, oldCache.SqlState);
        Assert.Throws<NotSupportedException>(() => new IsolateDemoProvenance().DownOperations);
    }

    // The legacy fields are unchanged owned entity columns. Bind values rather than constructing
    // SQL literals; exclude the new provenance column because the preceding schema has none.
    private static async Task InsertLegacySubsessionAsync(AppDbContext db, Subsession value)
    {
        var properties = db.Model.FindEntityType(typeof(Subsession))!.GetProperties()
            .Where(p => p.Name != nameof(Subsession.Provenance) && p.PropertyInfo!.GetValue(value) is not null).ToArray();
        var columns = string.Join(", ", properties.Select(p => $"\"{p.GetColumnName()}\""));
        var placeholders = string.Join(", ", Enumerable.Range(0, properties.Length)
            .Select(i => "{" + i.ToString(CultureInfo.InvariantCulture) + "}"));
        var values = properties.Select(p => p.PropertyInfo!.GetValue(value)!).ToArray();
        await db.Database.ExecuteSqlRawAsync(
            $"INSERT INTO iracing.\"Subsessions\" ({columns}) VALUES ({placeholders})", values, Ct);
    }
}
