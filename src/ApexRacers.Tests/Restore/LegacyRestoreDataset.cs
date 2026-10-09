using System.Globalization;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Data.Migrations;
using ApexRacers.Tests.References;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Tests.Restore;

/// <summary>Literal synthetic values inserted using the actual historical migration's owned columns.</summary>
internal static class LegacyRestoreDataset
{
    public static readonly DateTimeOffset Original = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public static readonly Guid Week = new("aaaaaaaa-3770-4000-8000-000000000010");
    public static async Task SeedAdditionalAsync(AppDbContext db, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO iracing."ExternalDataCaches" ("CacheKey", "Payload", "FetchedAt", "ExpiresAt")
            VALUES ('profile:100001', {0},
                    TIMESTAMPTZ '2020-01-01 00:00:00+00', TIMESTAMPTZ '9999-01-01 00:00:00+00'),
                   ('profile:100002', {1},
                    TIMESTAMPTZ '2020-01-01 00:00:00+00', TIMESTAMPTZ '2020-01-02 00:00:00+00');
            INSERT INTO iracing."UploadedLaps" ("Id", "UserId", "DriverCustId", "CarId", "TrackId", "LapTimeSeconds", "SessionType", "AirTempCelsius", "TrackTempCelsius", "TrackWetness", "RecordedAt")
            VALUES ('aaaaaaaa-3770-4000-8000-000000000002', 'aaaaaaaa-3750-4000-8000-000000000001', NULL, 40, 30, 94, 0, 20, 30, 0, TIMESTAMPTZ '2020-01-01 00:00:00+00'),
                   ('aaaaaaaa-3770-4000-8000-000000000003', 'aaaaaaaa-3750-4000-8000-000000000001', 100002, 40, 30, 95, 0, 20, 30, 0, TIMESTAMPTZ '2020-01-01 00:00:00+00');
            """, new object[] { "{\"DriverName\":\"Synthetic unknown legacy profile\"}", "{\"DriverName\":\"Synthetic unknown legacy expired profile\"}" }, ct);
        await InsertAsync(db, new Series { Id = 10, Name = "Synthetic ambiguous legacy series" }, ct);
        await InsertAsync(db, new Season { Id = 20, SeriesId = 10, Active = true }, ct);
        await InsertAsync(db, new CarClass { Id = 50, Name = "Synthetic legacy class", ShortName = "SLC" }, ct);
        await InsertAsync(db, new Week { Id = Week, SeasonId = 20, TrackId = 30, StartDate = new(2020, 1, 1), WeatherSummaryJson = "{\"syntheticUnknownWeather\":true}" }, ct);
        await InsertAsync(db, new Subsession
        {
            Id = -377,
            SeasonId = 20,
            WeekId = Week,
            TrackId = 30,
            StartTime = Original,
            OfficialSession = true,
            TeamEntryCount = 1,
            AiEntryCount = 1,
            SplitIndex = null,
            SplitCount = null
        }, ct);
        foreach (var customer in new[] { 100001L, 100002L })
            await InsertAsync(db, new SubsessionResult
            {
                SubsessionId = -377,
                CustId = customer,
                CarId = 40,
                CarClassId = 50,
                DisplayName = "Synthetic unknown legacy Driver " + customer,
                BestLapSeconds = 90 + customer % 2
            }, ct);
        await InsertAsync(db, new Rival
        {
            Id = Guid.NewGuid(),
            UserId = ReferenceActors.Recipient,
            RivalCustId = 100002,
            DisplayName = "Synthetic unknown legacy Follow",
            CreatedAt = Original
        }, ct);
        await InsertAsync(db, new CarPercentileResult
        {
            Id = Guid.NewGuid(),
            UserId = ReferenceActors.Recipient,
            CarId = 40,
            SeriesId = 10,
            WeekId = Week,
            PercentileRank = 50,
            TopSharePercent = 50,
            SampleSize = 2,
            ComputedAt = Original
        }, ct);
        await InsertAsync(db, new SeasonCarBop { SeasonId = 20, CarId = 40, WeightPenaltyKg = 1 }, ct);
    }

    private static async Task InsertAsync<T>(AppDbContext db, T value, CancellationToken ct) where T : class
    {
        // Historical EF model is the exact schema contract, not a provider response discovery mechanism.
        var entity = new AddKnownDevices().TargetModel.FindEntityType(typeof(T).FullName!)
            ?? throw new InvalidOperationException("Synthetic dataset type is missing from the pinned historical schema.");
        var properties = entity.GetProperties().Where(p => typeof(T).GetProperty(p.Name)?.GetValue(value) is not null).ToArray();
        var columns = string.Join(", ", properties.Select(p => $"\"{p.GetColumnName()}\""));
        var placeholders = string.Join(", ", Enumerable.Range(0, properties.Length).Select(i => "{" + i.ToString(CultureInfo.InvariantCulture) + "}"));
        var values = properties.Select(p => typeof(T).GetProperty(p.Name)!.GetValue(value)!).ToArray();
        // Names come exclusively from the checked-in historical migration model; values are bound.
#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync($"INSERT INTO \"{entity.GetSchema()}\".\"{entity.GetTableName()}\" ({columns}) VALUES ({placeholders})", values, ct);
#pragma warning restore EF1002
    }
}
