using ApexRacers.Api.Dtos;
using ApexRacers.Data;
using Aydsko.iRacingData.Tracks;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

/// <summary>
/// Browsable track catalog over the live iRacing API via <see cref="CachedIRacingClient"/> (24 h TTL).
/// Detail overlays the caller's personal best laps at that track when a user id is supplied.
/// </summary>
public class TrackCatalogService(CachedIRacingClient cached, AppDbContext db)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    public async Task<IReadOnlyList<TrackCatalogItemDto>> ListAsync(CancellationToken ct)
    {
        var tracks = await TracksAsync(ct);
        var assets = await AssetsAsync(ct);
        return tracks
            .Select(t => TrackCatalogMapper.ToItem(t, assets.GetValueOrDefault(t.TrackId.ToString())))
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.ConfigName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<TrackCatalogDetailDto> GetAsync(int trackId, Guid? userId, CancellationToken ct)
    {
        var tracks = await TracksAsync(ct);
        var track = tracks.FirstOrDefault(t => t.TrackId == trackId)
            ?? throw new KeyNotFoundException($"Track {trackId} was not found in the catalog.");
        var assets = await AssetsAsync(ct);

        IReadOnlyList<PersonalLapDto> bests = userId is { } uid
            ? await PersonalBestsForTrackAsync(uid, trackId, ct)
            : [];

        return TrackCatalogMapper.ToDetail(track, assets.GetValueOrDefault(trackId.ToString()), bests);
    }

    private Task<List<Track>> TracksAsync(CancellationToken ct) =>
        cached.GetOrFetchAsync("catalog:tracks", Ttl,
            async c => (await c.GetTracksAsync(ct)).Data.ToList(), ct);

    private Task<Dictionary<string, TrackAssets>> AssetsAsync(CancellationToken ct) =>
        cached.GetOrFetchAsync("catalog:track-assets", Ttl,
            async c => (await c.GetTrackAssetsAsync(ct)).Data.ToDictionary(k => k.Key, v => v.Value), ct);

    private async Task<List<PersonalLapDto>> PersonalBestsForTrackAsync(
        Guid userId, int trackId, CancellationToken ct)
    {
        var rows = await db.PersonalLaps
            .Where(l => l.UserId == userId && l.TrackId == trackId && l.IsValidLap)
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

        return rows
            .GroupBy(l => new { l.CarId, l.CarName, l.TrackName, l.ConfigName })
            .Select(g => new PersonalLapDto(
                g.Key.CarId, g.Key.CarName, g.Key.TrackName, g.Key.ConfigName,
                g.Min(l => l.LapTimeSeconds), g.Count(), g.Max(l => l.RecordedAt)))
            .OrderBy(d => d.BestLapSeconds)
            .ToList();
    }
}
