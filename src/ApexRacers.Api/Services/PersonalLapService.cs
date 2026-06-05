using ApexRacers.Api.Dtos;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

public class PersonalLapService(AppDbContext db)
{
    public async Task<List<PersonalLapDto>> GetPersonalBestsAsync(Guid userId, CancellationToken ct = default)
    {
        var rows = await db.PersonalLaps
            .Where(l => l.UserId == userId && l.IsValidLap)
            .Select(l => new {
                l.CarId,
                CarName    = l.Car.Name,
                TrackName  = l.Track.Name,
                ConfigName = l.Track.ConfigName,
                l.LapTimeSeconds,
                l.RecordedAt,
            })
            .ToListAsync(ct);

        return rows
            .GroupBy(l => new { l.CarId, l.CarName, l.TrackName, l.ConfigName })
            .Select(g => new PersonalLapDto(
                g.Key.CarId,
                g.Key.CarName,
                g.Key.TrackName,
                g.Key.ConfigName,
                g.Min(l => l.LapTimeSeconds),
                g.Count(),
                g.Max(l => l.RecordedAt)))
            .OrderByDescending(d => d.LastRecordedAt)
            .ToList();
    }
}
