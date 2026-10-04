using ApexRacers.Api.Dtos;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

/// <summary>
/// Browsable track catalog, read from the persisted <see cref="Core.Models.Track"/> catalog
/// (populated by the ingestion worker + seeder). Private upload overlays remain unavailable
/// until their ownership and protected publication workflow joins Driver Authorization.
/// </summary>
public class TrackCatalogService(AppDbContext db)
{
    public async Task<IReadOnlyList<TrackCatalogItemDto>> ListAsync(CancellationToken ct)
    {
        var tracks = await db.Tracks
            .AsNoTracking()
            .Where(t => !t.Retired)
            .OrderBy(t => t.Name)
            .ThenBy(t => t.ConfigName)
            .ToListAsync(ct);
        return tracks.Select(TrackCatalogMapper.ToItem).ToList();
    }

    public async Task<TrackCatalogDetailDto> GetAsync(int trackId, Guid? userId, CancellationToken ct)
    {
        var track = await db.Tracks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == trackId, ct)
            ?? throw new KeyNotFoundException($"Track {trackId} was not found in the catalog.");

        return TrackCatalogMapper.ToDetail(track, []);
    }
}
