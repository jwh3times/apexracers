using ApexRacers.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ApexRacers.Api.Controllers;

[ApiController]
[Route("api/series/{seriesId}/weeks/{weekNumber}")]
public class WeekController(WeekCarStatsService weekCarStats) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetWeekDetailAsync(int seriesId, int weekNumber, CancellationToken ct)
    {
        var result = await weekCarStats.GetWeekDetailAsync(seriesId, weekNumber, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("cars")]
    public async Task<IActionResult> GetCarsForWeekAsync(int seriesId, int weekNumber, CancellationToken ct)
        => Ok(await weekCarStats.GetCarsForWeekAsync(seriesId, weekNumber, ct));
}
