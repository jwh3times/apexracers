using ApexRacers.Api.Dtos;
using ApexRacers.Data;

namespace ApexRacers.Api.Services;

public class UploadedLapService(AppDbContext db)
{
    public Task<List<UploadedBestDto>> GetUploadedBestsAsync(Guid userId, CancellationToken ct = default) =>
        UploadedBestQuery.RunAsync(
            db.UploadedLaps.Where(l => l.UserId == userId),
            UploadedBestOrder.MostRecentFirst,
            ct);
}
