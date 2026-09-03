using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Ingestion;
using ApexRacers.Tests.Helpers;
using Aydsko.iRacingData.Series;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using WireTrack = Aydsko.iRacingData.Common.Track;

namespace ApexRacers.Tests.Ingestion;

public class SeasonIngestTests
{
    [Fact]
    public async Task UpsertHeaderAsync_InsertsThenUpdatesTheSharedSeasonFields()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = DbContextFactory.Create();
        var ingest = new SeasonIngest(db);
        var source = new SeasonSeries
        {
            SeriesId = 11,
            SeasonId = 501,
            SeasonYear = 2026,
            SeasonQuarter = 3,
            LicenseGroup = 4,
            Official = true,
            Drops = 3,
            FixedSetup = false,
            Multiclass = true,
        };

        await ingest.UpsertHeaderAsync(source, "GT Challenge", ct);
        db.ChangeTracker.Clear();

        var series = await db.Series.SingleAsync(ct);
        var season = await db.Seasons.SingleAsync(ct);
        Assert.Equal(11, series.Id);
        Assert.Equal("GT Challenge", series.Name);
        Assert.Equal(501, season.Id);
        Assert.Equal(11, season.SeriesId);
        Assert.Equal(2026, season.Year);
        Assert.Equal(3, season.Quarter);
        Assert.True(season.Active);
        Assert.Equal(4, season.LicenseGroup);
        Assert.True(season.Official);
        Assert.Equal(3, season.Drops);
        Assert.False(season.FixedSetup);
        Assert.True(season.Multiclass);

        source.LicenseGroup = 5;
        source.Official = false;
        source.Drops = 2;
        source.FixedSetup = true;
        source.Multiclass = false;
        season.Active = false;
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        await ingest.UpsertHeaderAsync(source, "GT Challenge Renamed", ct);
        db.ChangeTracker.Clear();

        Assert.Single(await db.Series.ToListAsync(ct));
        Assert.Single(await db.Seasons.ToListAsync(ct));
        series = await db.Series.SingleAsync(ct);
        season = await db.Seasons.SingleAsync(ct);
        Assert.Equal("GT Challenge Renamed", series.Name);
        Assert.True(season.Active);
        Assert.Equal(5, season.LicenseGroup);
        Assert.False(season.Official);
        Assert.Equal(2, season.Drops);
        Assert.True(season.FixedSetup);
        Assert.False(season.Multiclass);
    }

    [Fact]
    public async Task UpsertScheduleAsync_InsertsThenUpdatesWeekTrackAndBopWithoutDuplicates()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = DbContextFactory.Create();
        var ingest = new SeasonIngest(db);
        var item = ScheduleItem();

        await ingest.UpsertScheduleAsync(501, [item], ct);
        db.ChangeTracker.Clear();

        var track = await db.Tracks.SingleAsync(ct);
        var week = await db.Weeks.SingleAsync(ct);
        var bop = await db.SeasonCarBops.SingleAsync(ct);
        var car = await db.Cars.SingleAsync(ct);
        var seasonCar = await db.SeasonCars.SingleAsync(ct);
        Assert.Equal(18, track.Id);
        Assert.Equal("Spa", track.Name);
        Assert.Equal("GP", track.ConfigName);
        Assert.NotEqual(Guid.Empty, week.Id);
        Assert.Equal(501, week.SeasonId);
        Assert.Equal(7, week.RaceWeekIndex);
        Assert.Equal(18, week.TrackId);
        Assert.Equal(new DateOnly(2026, 8, 11), week.StartDate);
        Assert.Contains("\"temp_high\":26", week.WeatherSummaryJson);
        Assert.Equal(12.5, bop.WeightPenaltyKg);
        Assert.Equal(-2.25, bop.PowerAdjustPct);
        Assert.Equal(88.5, bop.MaxPctFuelFill);
        Assert.Equal(4, bop.MaxDryTireSets);
        Assert.Equal(132, car.Id);
        Assert.Equal("Merc GT3", car.Name);
        Assert.Equal("Merc", car.NameAbbreviated);
        Assert.Equal(501, seasonCar.SeasonId);
        Assert.Equal(132, seasonCar.CarId);

        item.Track.TrackName = "Spa Updated";
        item.Track.ConfigName = "N/A";
        item.StartDate = new DateOnly(2026, 8, 12);
        item.Weather.WeatherSummary!.TemperatureHigh = 29;
        item.CarRestrictions[0].WeightPenaltyKg = 15;
        item.CarRestrictions[0].PowerAdjustPercent = -1;
        item.CarRestrictions[0].MaxPercentFuelFill = 90;
        item.CarRestrictions[0].MaxDryTireSets = 5;
        db.ChangeTracker.Clear();

        await ingest.UpsertScheduleAsync(501, [item], ct);
        db.ChangeTracker.Clear();

        Assert.Single(await db.Tracks.ToListAsync(ct));
        Assert.Single(await db.Weeks.ToListAsync(ct));
        Assert.Single(await db.SeasonCarBops.ToListAsync(ct));
        Assert.Single(await db.Cars.ToListAsync(ct));
        Assert.Single(await db.SeasonCars.ToListAsync(ct));
        track = await db.Tracks.SingleAsync(ct);
        week = await db.Weeks.SingleAsync(ct);
        bop = await db.SeasonCarBops.SingleAsync(ct);
        Assert.Equal("Spa Updated", track.Name);
        Assert.Equal(string.Empty, track.ConfigName);
        Assert.Equal(new DateOnly(2026, 8, 12), week.StartDate);
        Assert.Contains("\"temp_high\":29", week.WeatherSummaryJson);
        Assert.Equal(15, bop.WeightPenaltyKg);
        Assert.Equal(-1, bop.PowerAdjustPct);
        Assert.Equal(90, bop.MaxPctFuelFill);
        Assert.Equal(5, bop.MaxDryTireSets);
    }

    [Fact]
    public async Task UpsertScheduleAsync_FlushesNewWeekBeforeAddingBopAndCars()
    {
        var ct = TestContext.Current.CancellationToken;
        var probe = new SaveOrderProbe();
        await using var connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        await connection.OpenAsync(ct);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(probe)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync(ct);

        var item = ScheduleItem();
        db.Tracks.Add(new Track { Id = item.Track.TrackId, Name = item.Track.TrackName });
        await db.SaveChangesAsync(ct);
        probe.Snapshots.Clear();

        await new SeasonIngest(db).UpsertScheduleAsync(501, [item], ct);

        Assert.True(probe.Snapshots.Count >= 2);
        var weekFlush = probe.Snapshots[0];
        Assert.NotEqual(Guid.Empty, weekFlush.WeekId);
        Assert.Equal(0, weekFlush.BopCount);
        Assert.Equal(0, weekFlush.CarCount);
        Assert.Contains(probe.Snapshots, snapshot =>
            snapshot.BopCount == 1 && snapshot.CarCount == 1);
    }

    [Fact]
    public async Task UpsertScheduleAsync_AllowsMissingWeatherRestrictionsAndCars()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = DbContextFactory.Create();
        var item = ScheduleItem();
        item.Weather = null!;
        item.CarRestrictions = null!;
        item.RaceWeekCars = [];

        await new SeasonIngest(db).UpsertScheduleAsync(501, [item], ct);

        Assert.Null((await db.Weeks.SingleAsync(ct)).WeatherSummaryJson);
        Assert.Empty(await db.SeasonCarBops.ToListAsync(ct));
        Assert.Empty(await db.Cars.ToListAsync(ct));
        Assert.Empty(await db.SeasonCars.ToListAsync(ct));
    }

    // ── week_end_time ─────────────────────────────────────────────────────────

    [Fact]
    public void EndTimeOrNull_ReportedEnd_IsKept()
    {
        var item = ScheduleItem();
        item.WeekEndTime = new DateTimeOffset(2026, 8, 17, 21, 0, 0, TimeSpan.Zero);

        Assert.Equal(item.WeekEndTime, SeasonIngest.EndTimeOrNull(item));
    }

    [Fact]
    public void EndTimeOrNull_OmittedField_IsNotEstablishedRatherThanYearOne()
    {
        // The SDK types week_end_time as non-nullable, so a payload without it deserializes to
        // default(DateTimeOffset) — a successful parse carrying 0001-01-01. Persisting that would
        // record every such Race Week as having closed two millennia before it opened.
        var item = ScheduleItem();
        item.WeekEndTime = default;

        Assert.Null(SeasonIngest.EndTimeOrNull(item));
    }

    [Fact]
    public void EndTimeOrNull_EndBeforeTheStart_IsRejected()
    {
        var item = ScheduleItem();                       // starts 2026-08-11
        item.WeekEndTime = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

        Assert.Null(SeasonIngest.EndTimeOrNull(item));
    }

    private static SeasonScheduleItem ScheduleItem() =>
        new()
        {
            RaceWeekNum = 7,
            StartDate = new DateOnly(2026, 8, 11),
            Track = new WireTrack
            {
                TrackId = 18,
                TrackName = "Spa",
                ConfigName = "GP",
            },
            Weather = new Weather
            {
                WeatherSummary = new WeatherSummary
                {
                    TemperatureHigh = 26,
                    TemperatureLow = 20,
                    TemperatureUnits = 1,
                },
            },
            CarRestrictions =
            [
                new CarRestrictions
                {
                    CarId = 132,
                    WeightPenaltyKg = 12.5m,
                    PowerAdjustPercent = -2.25m,
                    MaxPercentFuelFill = 88.5m,
                    MaxDryTireSets = 4,
                },
            ],
            RaceWeekCars =
            [
                new SeasonScheduleCar
                {
                    CarId = 132,
                    CarName = "Merc GT3",
                    CarNameAbbreviated = "Merc",
                },
            ],
        };

    private sealed class SaveOrderProbe : SaveChangesInterceptor
    {
        public List<SaveSnapshot> Snapshots { get; } = [];

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            var context = eventData.Context!;
            Snapshots.Add(new SaveSnapshot(
                context.ChangeTracker.Entries<Week>().SingleOrDefault()?.Entity.Id ?? Guid.Empty,
                context.ChangeTracker.Entries<SeasonCarBop>().Count(),
                context.ChangeTracker.Entries<Car>().Count()));
            return ValueTask.FromResult(result);
        }
    }

    private sealed record SaveSnapshot(Guid WeekId, int BopCount, int CarCount);
}
