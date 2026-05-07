using ApexRacers.Api.Dtos;
using ApexRacers.Data;

namespace ApexRacers.Api.Services;

public class PercentileCalculationService(AppDbContext db)
{
    // TODO: Fetch driver's best LapTimeEntry for carId/weekId; count entries that are slower
    //       to derive percentileRank (0–100, higher = faster relative to field); upsert the
    //       result into CarPercentileResult for caching; return PercentileResultDto
    public Task<PercentileResultDto> ComputeAndCacheAsync(
        int seriesId,
        int weekId,
        int carId,
        long customerId,
        CancellationToken ct = default)
        => throw new NotImplementedException();
}
