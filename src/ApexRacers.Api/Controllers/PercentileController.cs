using ApexRacers.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ApexRacers.Api.Controllers;

[ApiController]
[Route("api/series/{seriesId}/weeks/{weekNumber}/cars/{carId}/percentile")]
public class PercentileController(PercentileCalculationService percentile) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetPercentileAsync(
        int seriesId,
        int weekNumber,
        int carId,
        [FromQuery] long customerId,
        [FromQuery] bool includePersonalLaps = false,
        CancellationToken ct = default)
    {
        var result = await percentile.ComputeAndCacheAsync(seriesId, weekNumber, carId, customerId, includePersonalLaps, ct);
        return result is null ? NotFound() : Ok(result);
    }
}
