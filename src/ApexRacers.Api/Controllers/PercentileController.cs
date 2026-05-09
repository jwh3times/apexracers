using ApexRacers.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ApexRacers.Api.Controllers;

[ApiController]
[Route("api/series/{seriesId}/weeks/{weekId}/cars/{carId}/percentile")]
public class PercentileController(PercentileCalculationService percentile) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetPercentileAsync(
        int seriesId,
        int weekId,
        int carId,
        [FromQuery] long customerId,
        CancellationToken ct)
    {
        var result = await percentile.ComputeAndCacheAsync(seriesId, weekId, carId, customerId, ct);
        return result is null ? NotFound() : Ok(result);
    }
}
