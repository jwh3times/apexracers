using ApexRacers.Api.Dtos;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

public class SeriesService(AppDbContext db)
{
    public Task<List<SeriesDto>> GetActiveSeriesAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return db.Seasons
            .Where(s => s.Active)
            .Select(s => new SeriesDto(
                s.SeriesId,
                s.Series.Name,
                s.Id,
                s.Weeks
                    .Where(w => w.StartDate <= today)
                    .OrderByDescending(w => w.StartDate)
                    .Select(w => (int?)w.Id)
                    .FirstOrDefault()))
            .ToListAsync(ct);
    }
}
