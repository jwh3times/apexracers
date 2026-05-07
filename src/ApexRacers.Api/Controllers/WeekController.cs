using ApexRacers.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ApexRacers.Api.Controllers;

[ApiController]
[Route("api/series/{seriesId}/weeks/{weekId}/cars")]
public class WeekController(WeekCarStatsService weekCarStats) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetCarsForWeekAsync(int seriesId, int weekId, CancellationToken ct)
        => Ok(await weekCarStats.GetCarsForWeekAsync(seriesId, weekId, ct));
}
