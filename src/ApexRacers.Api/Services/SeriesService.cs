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
                    .Select(w => (int?)w.WeekNumber)
                    .FirstOrDefault(),
                s.Series.Category,
                s.Weeks
                    .Where(w => w.StartDate <= today)
                    .OrderByDescending(w => w.StartDate)
                    .Select(w => w.Track.Name)
                    .FirstOrDefault(),
                s.Weeks
                    .Where(w => w.StartDate <= today)
                    .OrderByDescending(w => w.StartDate)
                    .Select(w => w.Track.ConfigName)
                    .FirstOrDefault(),
                s.Weeks
                    .Where(w => w.StartDate <= today)
                    .OrderByDescending(w => w.StartDate)
                    .Take(1)
                    .SelectMany(w => w.Subsessions)
                    .Where(sub => sub.OfficialSession)
                    .SelectMany(sub => sub.Results)
                    .Select(r => r.CarId)
                    .Distinct()
                    .Count(),
                s.Weeks
                    .Where(w => w.StartDate <= today)
                    .OrderByDescending(w => w.StartDate)
                    .Take(1)
                    .SelectMany(w => w.Subsessions)
                    .Where(sub => sub.OfficialSession)
                    .SelectMany(sub => sub.Results)
                    .Select(r => r.CustId)
                    .Distinct()
                    .Count()))
            .ToListAsync(ct);
    }
}
