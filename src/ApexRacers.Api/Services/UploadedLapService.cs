using ApexRacers.Api.Dtos;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

public class UploadedLapService(AppDbContext db)
{
    public Task<List<UploadedBestDto>> GetUploadedBestsAsync(Guid userId, CancellationToken ct = default) =>
        UploadedBestQuery.RunAsync(
            // This is the User's upload inventory, outside synthetic Driver aggregates.
            db.UploadedLaps.IgnoreQueryFilters().Where(l => l.UserId == userId),
            UploadedBestOrder.MostRecentFirst,
            ct);
}
