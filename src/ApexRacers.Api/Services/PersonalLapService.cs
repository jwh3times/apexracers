using ApexRacers.Api.Dtos;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

public class PersonalLapService(AppDbContext db)
{
    public Task<List<PersonalLapDto>> GetPersonalBestsAsync(long customerId, CancellationToken ct = default) =>
        db.PersonalLaps
            .Where(l => l.User.IRacingCustomerId == customerId && l.IsValidLap)
            .GroupBy(l => new { l.CarId, l.Car.Name, l.TrackName, l.ConfigName })
            .Select(g => new PersonalLapDto(
                g.Key.CarId,
                g.Key.Name,
                g.Key.TrackName,
                g.Key.ConfigName,
                g.Min(l => l.LapTimeSeconds),
                g.Count(),
                g.Max(l => l.RecordedAt)))
            .OrderByDescending(d => d.LastRecordedAt)
            .ToListAsync(ct);
}
