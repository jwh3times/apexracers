using ApexRacers.Api.Dtos;
using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

/// <summary>How to order a personal-best list. Required, because no ordering is never correct.</summary>
public enum PersonalBestOrder
{
    /// <summary>Fastest lap first — the catalog pages' "your best laps" overlay.</summary>
    FastestFirst,

    /// <summary>Most recently driven first — the My Laps page.</summary>
    MostRecentFirst,
}

/// <summary>
/// Collapses a scope of personal laps into one best-lap row per car + track configuration.
///
/// <para><b>Why this exists.</b> The same twenty-line projection was written three times — for the
/// My Laps page, the car catalog detail page, and the track catalog detail page — differing only
/// in the filter and the sort. <see cref="PersonalLapDto"/> is a positional record with seven
/// fields, so adding one meant editing three places and a mis-ordered argument at any of them
/// would have been a runtime data bug rather than a compile error.</para>
///
/// <para>Each copy also independently carried the knowledge that the grouping <b>must not</b> be
/// pushed to SQL — a fact written down in none of them. See the materialization note below.</para>
/// </summary>
public static class PersonalBestQuery
{
    /// <summary>
    /// Runs <paramref name="scope"/> and reduces it to per-car-and-track bests.
    /// </summary>
    /// <param name="scope">
    /// The laps to consider, filtered to a single user plus whatever else narrows the view (a car,
    /// a track, nothing). <b>Do not filter on <c>IsValidLap</c></b> — this module applies it, so
    /// no caller can forget to and quietly report an invalid lap as a personal best.
    /// </param>
    /// <param name="order">How to sort the result. See <see cref="PersonalBestOrder"/>.</param>
    public static async Task<List<PersonalLapDto>> RunAsync(
        IQueryable<PersonalLap> scope,
        PersonalBestOrder order,
        CancellationToken ct = default)
    {
        // Materialize before grouping, deliberately. The group key spans navigation properties
        // (Car.Name, Track.Name, Track.ConfigName) alongside the aggregates, which neither Npgsql
        // nor SQLite will translate — pushing the GroupBy into SQL throws at runtime rather than
        // failing to compile. The row count here is one user's uploaded laps, so this is cheap.
        var rows = await scope
            .Where(l => l.IsValidLap)
            .Select(l => new
            {
                l.CarId,
                CarName = l.Car.Name,
                TrackName = l.Track.Name,
                ConfigName = l.Track.ConfigName,
                l.LapTimeSeconds,
                l.RecordedAt,
            })
            .ToListAsync(ct);

        var bests = rows
            .GroupBy(l => new { l.CarId, l.CarName, l.TrackName, l.ConfigName })
            .Select(g => new PersonalLapDto(
                g.Key.CarId,
                g.Key.CarName,
                g.Key.TrackName,
                g.Key.ConfigName,
                g.Min(l => l.LapTimeSeconds),
                g.Count(),
                g.Max(l => l.RecordedAt)));

        return order switch
        {
            PersonalBestOrder.FastestFirst => bests.OrderBy(d => d.BestLapSeconds).ToList(),
            PersonalBestOrder.MostRecentFirst => bests.OrderByDescending(d => d.LastRecordedAt).ToList(),
            _ => throw new ArgumentOutOfRangeException(nameof(order), order, "Unknown ordering."),
        };
    }
}
