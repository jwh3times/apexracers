using ApexRacers.Api.Dtos;
using ApexRacers.Core;
using ApexRacers.Data;
using Aydsko.iRacingData;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

/// <summary>
/// Championship driver, Time Trial standings, and weekly qualifying results for a series'
/// active season, fetched on demand through <see cref="CachedIRacingClient"/> (24-hour TTL).
/// The season and the set of car classes are resolved from the local catalog; the caller picks
/// a class (defaults to the first). Driver/TT standings come straight from the SDK; qualifying
/// results are downloaded from the response header's chunk_info and parsed by
/// <see cref="QualifyResultsParser"/> because the SDK's <c>SeasonQualifyResult</c> type does not
/// model the real payload (it omits the qualifying lap time). Only the top <see cref="TopN"/>
/// rows are kept and cached.
/// </summary>
public class StandingsService(AppDbContext db, CachedIRacingClient cached, IChunkDownloader chunks)
{
    public const int TopN = 100;

    public async Task<SeasonStandingsDto> GetDriverStandingsAsync(
        int seriesId, int? carClassId, CancellationToken ct)
    {
        var ctx = await ResolveAsync(seriesId, carClassId, ct);

        var standings = await cached.GetOrFetchAsync<IReadOnlyList<SeasonStandingDto>>(
            IRacingCacheKeys.Standings(ctx.SeasonId, ctx.ClassId),
            async c =>
            {
                var rows = (await c.GetSeasonDriverStandingsAsync(
                    ctx.SeasonId, ctx.ClassId, null, null, ct)).Data.Item2;
                return (rows ?? [])
                    .OrderBy(s => s.Rank)
                    .Take(TopN)
                    .Select(s => new SeasonStandingDto(
                        s.Rank, s.CustomerId, s.DisplayName, s.Division,
                        s.Starts, s.Wins, s.Top5, s.Poles, s.Points,
                        (double)s.AverageFinishPosition, s.Incidents))
                    .ToList();
            },
            ct);

        return new SeasonStandingsDto(
            seriesId, ctx.SeriesName, ctx.ClassId, ctx.ClassName, ctx.Classes, standings);
    }

    public async Task<SeasonTtStandingsDto> GetTimeTrialStandingsAsync(
        int seriesId, int? carClassId, CancellationToken ct)
    {
        var ctx = await ResolveAsync(seriesId, carClassId, ct);

        var standings = await cached.GetOrFetchAsync<IReadOnlyList<SeasonTtStandingDto>>(
            IRacingCacheKeys.TimeTrialStandings(ctx.SeasonId, ctx.ClassId),
            async c =>
            {
                var rows = (await c.GetSeasonTimeTrialStandingsAsync(
                    ctx.SeasonId, ctx.ClassId, null, null, ct)).Data.Item2;
                return (rows ?? [])
                    .OrderBy(s => s.Rank)
                    .Take(TopN)
                    .Select(s => new SeasonTtStandingDto(
                        s.Rank, s.CustomerId, s.DisplayName, s.Division,
                        s.License?.TimeTrialRating,
                        s.Starts, s.Wins, s.Top5, s.Poles, s.Points,
                        (double)s.AverageFinishPosition, s.Incidents))
                    .ToList();
            },
            ct);

        return new SeasonTtStandingsDto(
            seriesId, ctx.SeriesName, ctx.ClassId, ctx.ClassName, ctx.Classes, standings);
    }

    public async Task<SeasonQualifyResultsDto> GetQualifyResultsAsync(
        int seriesId, int? carClassId, int? raceWeekNum, CancellationToken ct)
    {
        var ctx = await ResolveAsync(seriesId, carClassId, ct);

        var weeks = await db.Weeks
            .Where(w => w.SeasonId == ctx.SeasonId)
            .OrderBy(w => w.WeekNumber)
            .Select(w => new { w.WeekNumber, w.StartDate })
            .ToListAsync(ct);
        var availableWeeks = weeks.Select(w => w.WeekNumber).ToList();
        var week = raceWeekNum ?? CurrentWeek(weeks.Select(w => (w.WeekNumber, w.StartDate)));

        var results = await cached.GetOrFetchAsync<IReadOnlyList<SeasonQualifyResultDto>>(
            IRacingCacheKeys.QualifyResults(ctx.SeasonId, ctx.ClassId, week),
            async c =>
            {
                // The SDK type drops the lap time, so we only use it for the authenticated first
                // hop: the header carries fresh signed chunk URLs we download + parse ourselves.
                var header = (await c.GetSeasonQualifyResultsAsync(
                    ctx.SeasonId, ctx.ClassId, week, null, ct)).Data.Item1;
                var info = header.ChunkInfo;
                if (info?.ChunkFileNames is not { Length: > 0 })
                    return [];

                var docs = await chunks.DownloadAsync(info.BaseDownloadUrl, info.ChunkFileNames, ct);
                return QualifyResultsParser.Parse(docs)
                    .OrderBy(r => r.Rank)
                    .Take(TopN)
                    .ToList();
            },
            ct);

        return new SeasonQualifyResultsDto(
            seriesId, ctx.SeriesName, ctx.ClassId, ctx.ClassName, ctx.Classes,
            week, availableWeeks, results);
    }

    /// <summary>
    /// The week in progress, falling back to the first known week before the season starts —
    /// a standings page has to render some week, so unlike the series list it cannot show nothing.
    /// The selection rule itself lives in <see cref="SeasonCalendar"/>.
    /// </summary>
    private static int CurrentWeek(IEnumerable<(int WeekNumber, DateOnly StartDate)> weeks)
    {
        var list = weeks.ToList();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return SeasonCalendar.CurrentWeekNumber(list, today)
            ?? (list.Count > 0 ? list.Min(w => w.WeekNumber) : 0);
    }

    private sealed record ResolvedContext(
        int SeasonId, string SeriesName, int ClassId, string ClassName,
        IReadOnlyList<CarClassOptionDto> Classes);

    private async Task<ResolvedContext> ResolveAsync(int seriesId, int? carClassId, CancellationToken ct)
    {
        var season = await db.ActiveSeasonOrThrowAsync(seriesId, ct);
        var seriesName = await db.SeriesNameAsync(seriesId, ct);

        // Order by the CarClass column *before* projecting to the DTO. Ordering by a positional
        // record's property after projection doesn't translate to SQL (the provider can't map the
        // DTO member back to the column) — it fails on Npgsql in production, not just SQLite.
        var classes = await db.SeasonCarClasses
            .Where(sc => sc.SeasonId == season.Id)
            .Join(db.CarClasses, sc => sc.CarClassId, cc => cc.Id, (sc, cc) => cc)
            .OrderBy(cc => cc.Name)
            .Select(cc => new CarClassOptionDto(cc.Id, cc.Name))
            .ToListAsync(ct);

        var selectedClassId = carClassId ?? (classes.Count > 0 ? classes[0].CarClassId : (int?)null);
        if (selectedClassId is null)
            throw new KeyNotFoundException($"No car classes available for series {seriesId}.");

        var className = classes.FirstOrDefault(c => c.CarClassId == selectedClassId)?.CarClassName
            ?? string.Empty;

        return new ResolvedContext(season.Id, seriesName, selectedClassId.Value, className, classes);
    }
}
