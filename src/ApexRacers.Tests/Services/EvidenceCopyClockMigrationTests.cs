using System.Globalization;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace ApexRacers.Tests.Services;

[Collection(PostgreSqlCollection.Name)]
public sealed class EvidenceCopyClockMigrationTests(PostgreSqlFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string PreviousMigration = "20261015011526_ClassifyPercentileHistory";

    [Fact]
    public async Task PopulatedPreviousSchemaUpgradesWithoutRewritingClocksOrPromotingUnrelatedEvidence()
    {
        var options = await postgres.CreateOptionsAsync(Ct);
        await using var db = new AppDbContext(options, new IRacingDataScope(DataProvenance.Demo));
        await db.Database.EnsureDeletedAsync(Ct);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260912044021_AddKnownDevices", Ct);
        db.Series.Add(new() { Id = 10, Name = "Historical synthetic Series" });
        db.Seasons.Add(new() { Id = 20, SeriesId = 10, Year = 2020, Quarter = 1 });
        db.Tracks.Add(new() { Id = 30, Name = "Historical synthetic Track" });
        db.Cars.Add(new() { Id = 40, Name = "Historical synthetic Car", NameAbbreviated = "HC" });
        db.CarClasses.Add(new() { Id = 50, Name = "Historical synthetic Class", ShortName = "HC" });
        await db.SaveChangesAsync(Ct);
        var originalTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await InsertHistoricalAsync(db, "Subsessions", new Subsession
        { Id = -70, SeasonId = 20, TrackId = 30, StartTime = originalTime });
        await InsertHistoricalAsync(db, "SubsessionResults", new SubsessionResult
        { SubsessionId = -70, CustId = 700, CarId = 40, CarClassId = 50, BestLapSeconds = 123 });
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO iracing."ExternalDataCaches" ("CacheKey", "Payload", "FetchedAt", "ExpiresAt")
            VALUES ('legacy:unclassified', json_build_object()::text, TIMESTAMPTZ '2020-01-01', TIMESTAMPTZ '9999-01-01');
            """, Ct);
        await migrator.MigrateAsync("20261003030330_IsolateDemoProvenance", Ct);
        // These historical rows precede lifecycle fences; no new receipt or authority is invented.
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO iracing."MappedDataCaches" ("Provenance", "CacheKey", "Payload", "FetchedAt", "ExpiresAt")
            VALUES (1, 'legacy:real', json_build_object()::text, TIMESTAMPTZ '2020-01-01', TIMESTAMPTZ '9999-01-01');
            """, Ct);
        await migrator.MigrateAsync(PreviousMigration, Ct);
        var user = Guid.NewGuid();
        db.Users.Add(new() { Id = user, DisplayName = "Synthetic historical User", EmailConfirmed = true });
        db.Series.Add(new() { Id = 1, Name = "Synthetic Series" });
        db.Seasons.Add(new() { Id = 1, SeriesId = 1, Year = 2026, Quarter = 1, Active = true });
        db.Tracks.Add(new() { Id = 1, Name = "Synthetic Track" });
        db.Cars.Add(new() { Id = 1, Name = "Synthetic Car", NameAbbreviated = "SC" });
        var week = Guid.NewGuid();
        db.Weeks.Add(new() { Id = week, SeasonId = 1, TrackId = 1 });
        db.UploadedLaps.Add(new()
        {
            Id = Guid.NewGuid(),
            UserId = user,
            DriverCustId = 700,
            CarId = 1,
            TrackId = 1,
            RecordedAt = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            LapTimeSeconds = 123
        });
        await db.SaveChangesAsync(Ct);
        var clock = new HistoricalClock(DriverAuthorizationPolicy.DurableTime(TimeProvider.System.GetUtcNow().AddDays(-20)));
        var lifecycle = new EvidenceCopyLifecycle(db, clock);
        var official = await lifecycle.OpenSyntheticPurposeAsync(EvidencePurposeKind.IndependentOfficial, 1, ct: Ct);
        var weather = await lifecycle.CaptureAsync(official, EvidenceCopyKind.Weather, "weather", ct: Ct);
        var source = await lifecycle.CommitAsync(weather, new WeatherBatch(week, "{}"), Ct);
        var preview = await lifecycle.OpenSyntheticPurposeAsync(ct: Ct);
        var receipt = await lifecycle.CaptureAsync(preview, EvidenceCopyKind.MappedCache, "overdue", [source], Ct);
        var copy = await lifecycle.CommitAsync(receipt, new MappedCacheBatch(new()
        {
            CacheKey = receipt.KeyHash,
            Payload = "{}",
            FetchedAt = receipt.OriginalAcquiredAt,
            ExpiresAt = receipt.OriginalAcquiredAt.AddHours(1)
        }), Ct);
        var original = await db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(m => m.Id == copy.CopyId, Ct);
        var fullBefore = await StateHashAsync(db);
        var unrelatedBefore = await UnrelatedHashAsync(db);
        var oldFailure = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE iracing.\"Weeks\" SET \"DemoWeatherSummaryJson\" = NULL", Ct));
        Assert.Equal("23514", oldFailure.SqlState);
        Assert.Equal("CK_EvidenceCopy_Clock", oldFailure.ConstraintName);
        Assert.Equal(fullBefore, await StateHashAsync(db)); // The failed original transition rolled back completely.

        await migrator.MigrateAsync(cancellationToken: Ct);
        Assert.Equal(fullBefore, await StateHashAsync(db)); // Migration replaces code, never historical clocks/data.
        await db.Database.ExecuteSqlRawAsync("UPDATE iracing.\"Weeks\" SET \"DemoWeatherSummaryJson\" = NULL", Ct);
        var after = await db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(m => m.Id == copy.CopyId, Ct);
        Assert.Equal(original.OriginalAcquiredAt, after.OriginalAcquiredAt);
        Assert.Equal(original.ExpiresAt, after.ExpiresAt);
        Assert.Equal(original.ExpiresAt, after.UnavailableAt);
        Assert.Equal(original.RemovalDueAt, after.RemovalDueAt);
        Assert.Equal(unrelatedBefore, await UnrelatedHashAsync(db));
        Assert.Empty(await db.ExternalDataCaches.ToListAsync(Ct));
        await Assert.ThrowsAsync<NotSupportedException>(() => migrator.MigrateAsync(PreviousMigration, Ct));
        Assert.Contains("20261018124545_PreserveEvidenceCopyClocks", await db.Database.GetAppliedMigrationsAsync(Ct));
        Assert.Equal(unrelatedBefore, await UnrelatedHashAsync(db));
    }

    private static Task<string> StateHashAsync(AppDbContext db) => db.Database.SqlQueryRaw<string>("""
        SELECT md5(string_agg(payload, E'\n' ORDER BY payload)) AS "Value" FROM (
            SELECT 'markers:' || row_to_json(t)::text AS payload FROM iracing."EvidenceCopyMarkers" t
            UNION ALL SELECT 'purposes:' || row_to_json(t)::text FROM iracing."EvidencePurposes" t
            UNION ALL SELECT 'weeks:' || row_to_json(t)::text FROM iracing."Weeks" t
            UNION ALL SELECT 'cache:' || row_to_json(t)::text FROM iracing."MappedDataCaches" t
            UNION ALL SELECT 'quarantine:' || row_to_json(t)::text FROM iracing."QuarantinedDataCaches" t
            UNION ALL SELECT 'uploads:' || row_to_json(t)::text FROM iracing."QuarantinedUploadedLaps" t
            UNION ALL SELECT 'unknown-race:' || row_to_json(t)::text FROM iracing."RaceEvidenceSubsessions" t WHERE "Provenance" = 0
            UNION ALL SELECT 'unknown-results:' || row_to_json(t)::text FROM iracing."RaceEvidenceResults" t WHERE "Provenance" = 0
        ) retained
        """).SingleAsync(Ct);

    private static Task<string> UnrelatedHashAsync(AppDbContext db) => db.Database.SqlQueryRaw<string>("""
        SELECT md5(string_agg(payload, E'\n' ORDER BY payload)) AS "Value" FROM (
            SELECT 'cache:' || row_to_json(t)::text AS payload FROM iracing."MappedDataCaches" t WHERE "Provenance" <> 2
            UNION ALL SELECT 'quarantine:' || row_to_json(t)::text FROM iracing."QuarantinedDataCaches" t
            UNION ALL SELECT 'uploads:' || row_to_json(t)::text FROM iracing."QuarantinedUploadedLaps" t
            UNION ALL SELECT 'unknown-race:' || row_to_json(t)::text FROM iracing."RaceEvidenceSubsessions" t WHERE "Provenance" = 0
            UNION ALL SELECT 'unknown-results:' || row_to_json(t)::text FROM iracing."RaceEvidenceResults" t WHERE "Provenance" = 0
        ) retained
        """).SingleAsync(Ct);

    private static Task InsertHistoricalAsync<T>(AppDbContext db, string table, T value) where T : class
    {
        var properties = db.Model.FindEntityType(typeof(T))!.GetProperties()
            .Where(p => p.Name is not (nameof(IManagedEvidence.EvidenceCopyId) or nameof(IProvenancedData.Provenance))
                && p.PropertyInfo!.GetValue(value) is not null).ToArray();
        var columns = string.Join(", ", properties.Select(p => $"\"{p.GetColumnName()}\""));
        var placeholders = string.Join(", ", Enumerable.Range(0, properties.Length)
            .Select(i => "{" + i.ToString(CultureInfo.InvariantCulture) + "}"));
        return db.Database.ExecuteSqlRawAsync($"INSERT INTO iracing.\"{table}\" ({columns}) VALUES ({placeholders})",
            properties.Select(p => p.PropertyInfo!.GetValue(value)!).ToArray(), Ct);
    }

    private sealed class HistoricalClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
