using ApexRacers.Api.Dtos;
using ApexRacers.Data;

namespace ApexRacers.Api.Services;

public class CarRecommendationService(AppDbContext db)
{
    // TODO: For each Car that ran in weekId, retrieve the cached CarPercentileResult for
    //       customerId (or delegate to PercentileCalculationService if not yet cached);
    //       sort by percentileRank descending; assign rank 1..N; map to CarRecommendationDto list
    public Task<List<CarRecommendationDto>> GetRecommendationsAsync(
        int weekId,
        long customerId,
        CancellationToken ct = default)
        => throw new NotImplementedException();
}
