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

        IReadOnlyList<PersonalLapDto> bests = userId is { } uid
            ? await PersonalBestsForTrackAsync(uid, trackId, ct)
            : [];

        return TrackCatalogMapper.ToDetail(track, bests);
    }

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
