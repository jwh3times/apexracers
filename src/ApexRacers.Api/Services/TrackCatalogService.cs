using ApexRacers.Api.Dtos;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

/// <summary>
/// Browsable track catalog, read from the persisted <see cref="Core.Models.Track"/> catalog
/// (populated by the ingestion worker + seeder). Detail overlays the caller's personal best laps
/// at that track when a user id is supplied.
/// </summary>
public class TrackCatalogService(AppDbContext db)
{
    public async Task<IReadOnlyList<TrackCatalogItemDto>> ListAsync(CancellationToken ct)
    {
        var tracks = await db.Tracks
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .ThenBy(t => t.ConfigName)
            .ToListAsync(ct);
        return tracks.Select(TrackCatalogMapper.ToItem).ToList();
    }

    public async Task<TrackCatalogDetailDto> GetAsync(int trackId, Guid? userId, CancellationToken ct)
    {
        var track = await db.Tracks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == trackId, ct)
            ?? throw new KeyNotFoundException($"Track {trackId} was not found in the catalog.");

        IReadOnlyList<UploadedBestDto> bests = userId is { } uid
            ? await PersonalBestsForTrackAsync(uid, trackId, ct)
            : [];

        return TrackCatalogMapper.ToDetail(track, bests);
    }

    private Task<List<UploadedBestDto>> PersonalBestsForTrackAsync(
        Guid userId, int trackId, CancellationToken ct) =>
        UploadedBestQuery.RunAsync(
            db.UploadedLaps.Where(l => l.UserId == userId && l.TrackId == trackId),
            UploadedBestOrder.FastestFirst,
            ct);
}
