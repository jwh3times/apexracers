using ApexRacers.Api.Dtos;
using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

/// <summary>How to order an Uploaded Best list. Required, because no ordering is never correct.</summary>
public enum UploadedBestOrder
{
    /// <summary>Fastest lap first — the catalog pages' uploaded-bests overlay.</summary>
    FastestFirst,

    /// <summary>Most recently driven first — the My Laps page.</summary>
    MostRecentFirst,
}

/// <summary>
/// Collapses a scope of Uploaded Laps into one Uploaded Best row per car + track configuration.
///
/// <para><b>Why this exists.</b> The same twenty-line projection was written three times — for the
/// My Laps page, the car catalog detail page, and the track catalog detail page — differing only
/// in the filter and the sort. <see cref="UploadedBestDto"/> is a positional record with seven
/// fields, so adding one meant editing three places and a mis-ordered argument at any of them
/// would have been a runtime data bug rather than a compile error.</para>
///
/// <para>Each copy also independently carried the knowledge that the grouping <b>must not</b> be
/// pushed to SQL — a fact written down in none of them. See the materialization note below.</para>
/// </summary>
public static class UploadedBestQuery
{
    /// <summary>
    /// Runs <paramref name="scope"/> and reduces it to per-car-and-track bests.
    /// </summary>
    /// <param name="scope">
    /// The laps to consider, filtered to a single user plus whatever else narrows the view (a car,
    /// a track, nothing). <b>Do not filter on <c>IsValidLap</c></b> — this module applies it, so
    /// no caller can forget to and quietly report an invalid lap as an Uploaded Best.
    /// </param>
    /// <param name="order">How to sort the result. See <see cref="UploadedBestOrder"/>.</param>
    public static async Task<List<UploadedBestDto>> RunAsync(
        IQueryable<UploadedLap> scope,
        UploadedBestOrder order,
        CancellationToken ct = default)
    {
        // Materialize before grouping, deliberately. The projection reaches through navigation
        // properties (Car.Name, Track.Name, Track.ConfigName) for the labels, and selecting those
        // alongside the aggregates is what neither Npgsql nor SQLite will translate — pushing the
        // GroupBy into SQL throws at runtime rather than failing to compile. Keying on identifiers
        // rather than names does not change that. The row count here is one user's uploaded laps,
        // so this is cheap.
        var rows = await scope
            .Where(l => l.IsValidLap)
            .Select(l => new
            {
                l.CarId,
                CarName = l.Car.Name,
                l.TrackId,
                TrackName = l.Track.Name,
                ConfigName = l.Track.ConfigName,
                l.LapTimeSeconds,
                l.RecordedAt,
            })
            .ToListAsync(ct);

        // Key on the identifiers, never the labels. A Track Name belongs to the venue and is
        // shared by every layout there, so a name-keyed group is correct only for as long as
        // iRacing happens to label the configurations apart — and eight tracks are already named
        // with a trailing space, which any later whitespace normalization would silently merge.
        // The names ride along as labels, taken from the first row of each group.
        var bests = rows
            .GroupBy(l => new { l.CarId, l.TrackId })
            .Select(g => new UploadedBestDto(
                g.Key.CarId,
                g.First().CarName,
                g.Key.TrackId,
                g.First().TrackName,
                g.First().ConfigName,
                g.Min(l => l.LapTimeSeconds),
                g.Count(),
                g.Max(l => l.RecordedAt)));

        return order switch
        {
            UploadedBestOrder.FastestFirst => bests.OrderBy(d => d.BestLapSeconds).ToList(),
            UploadedBestOrder.MostRecentFirst => bests.OrderByDescending(d => d.LastRecordedAt).ToList(),
            _ => throw new ArgumentOutOfRangeException(nameof(order), order, "Unknown ordering."),
        };
    }
}
