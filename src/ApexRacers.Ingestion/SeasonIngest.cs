using System.Text.Json;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using Aydsko.iRacingData.Series;
using Microsoft.EntityFrameworkCore;
using CoreSeries = ApexRacers.Core.Models.Series;

namespace ApexRacers.Ingestion;

/// <summary>
/// Owns the relational upsert rules for one season header and its schedule. The worker supplies
/// fetched SDK data; this module owns insert/update parity and dependency-ordered persistence.
/// </summary>
public sealed class SeasonIngest(AppDbContext db)
{
    public async Task UpsertHeaderAsync(
        SeasonSeries source,
        string seriesName,
        CancellationToken ct = default)
    {
        var series = await db.Series.FindAsync([source.SeriesId], ct);
        if (series is null)
        {
            series = new CoreSeries { Id = source.SeriesId, Name = seriesName };
            db.Series.Add(series);
        }

        series.Name = seriesName;

        var season = await db.Seasons.FindAsync([source.SeasonId], ct);
        if (season is null)
        {
            season = new Season
            {
                Id = source.SeasonId,
                SeriesId = source.SeriesId,
                Year = source.SeasonYear,
                Quarter = source.SeasonQuarter,
            };
            db.Seasons.Add(season);
        }

        PopulateSeason(season, source);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpsertScheduleAsync(
        int seasonId,
        IReadOnlyCollection<SeasonScheduleItem> schedule,
        CancellationToken ct = default)
    {
        foreach (var item in schedule)
        {
            var track = await db.Tracks.FindAsync([item.Track.TrackId], ct);
            if (track is null)
            {
                track = new Track { Id = item.Track.TrackId, Name = item.Track.TrackName };
                db.Tracks.Add(track);
                PopulateTrack(track, item);
                await db.SaveChangesAsync(ct);
            }
            else
            {
                PopulateTrack(track, item);
            }

            var weatherJson = WeatherIngest.ToSnapshot(item.Weather?.WeatherSummary) is { } forecast
                ? JsonSerializer.Serialize(forecast)
                : null;

            var week = await db.Weeks.FirstOrDefaultAsync(
                candidate => candidate.SeasonId == seasonId
                    && candidate.WeekNumber == item.RaceWeekNum,
                ct);

            if (week is null)
            {
                week = new Week
                {
                    SeasonId = seasonId,
                    WeekNumber = item.RaceWeekNum,
                };
                db.Weeks.Add(week);
                PopulateWeek(week, item, weatherJson);

                // Week.Id is generated on insert. Flush before continuing with this schedule item
                // so later ingestion can safely resolve the persisted Week FK by season/week.
                await db.SaveChangesAsync(ct);
            }
            else
            {
                PopulateWeek(week, item, weatherJson);
            }

            foreach (var restriction in item.CarRestrictions ?? [])
            {
                var bop = await db.SeasonCarBops.FindAsync(
                    [seasonId, item.RaceWeekNum, restriction.CarId], ct);
                if (bop is null)
                {
                    bop = new SeasonCarBop
                    {
                        SeasonId = seasonId,
                        WeekNumber = item.RaceWeekNum,
                        CarId = restriction.CarId,
                    };
                    db.SeasonCarBops.Add(bop);
                }

                PopulateBop(bop, restriction);
            }

            foreach (var sourceCar in item.RaceWeekCars)
            {
                if (await db.Cars.FindAsync([sourceCar.CarId], ct) is null)
                {
                    db.Cars.Add(new Car
                    {
                        Id = sourceCar.CarId,
                        Name = sourceCar.CarName,
                        NameAbbreviated = sourceCar.CarNameAbbreviated,
                    });
                }

                if (await db.SeasonCars.FindAsync([seasonId, sourceCar.CarId], ct) is null)
                {
                    db.SeasonCars.Add(new SeasonCar
                    {
                        SeasonId = seasonId,
                        CarId = sourceCar.CarId,
                    });
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static void PopulateSeason(Season season, SeasonSeries source)
    {
        season.Active = true;
        season.LicenseGroup = source.LicenseGroup;
        season.Official = source.Official;
        season.Drops = source.Drops;
        season.FixedSetup = source.FixedSetup;
        season.Multiclass = source.Multiclass;
    }

    private static void PopulateTrack(Track track, SeasonScheduleItem source)
    {
        track.Name = source.Track.TrackName;
        track.ConfigName = source.Track.ConfigName ?? string.Empty;
    }

    private static void PopulateWeek(Week week, SeasonScheduleItem source, string? weatherJson)
    {
        week.TrackId = source.Track.TrackId;
        week.StartDate = source.StartDate;
        week.EndTime = EndTimeOrNull(source);
        week.WeatherSummaryJson = weatherJson;
    }

    /// <summary>
    /// iRacing's exact <c>week_end_time</c>, or null when the payload did not supply a usable one.
    /// </summary>
    /// <remarks>
    /// The SDK types this as a non-nullable <see cref="DateTimeOffset"/>, so a payload that omits
    /// the field deserializes to <c>default</c> (<c>0001-01-01</c>) rather than throwing — a
    /// successful parse carrying a value that is not a date. Persisting that would make every such
    /// Week look like it closed two millennia before it opened. Anything not after the Week's start
    /// is therefore stored as "not established," which is what null means on the column, and
    /// <c>RaceWeekWindow</c> derives a boundary instead.
    /// </remarks>
    public static DateTimeOffset? EndTimeOrNull(SeasonScheduleItem source)
    {
        var start = new DateTimeOffset(source.StartDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return source.WeekEndTime > start ? source.WeekEndTime : null;
    }

    private static void PopulateBop(SeasonCarBop bop, CarRestrictions source)
    {
        bop.WeightPenaltyKg = (double)source.WeightPenaltyKg;
        bop.PowerAdjustPct = (double)source.PowerAdjustPercent;
        bop.MaxPctFuelFill = (double)source.MaxPercentFuelFill;
        bop.MaxDryTireSets = source.MaxDryTireSets;
    }
}
