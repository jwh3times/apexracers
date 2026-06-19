using ApexRacers.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ApexRacers.Api.Controllers;

// Public — championship standings are official, non-sensitive data (mirrors WeekController).
[ApiController]
[Route("api/series")]
public class StandingsController(StandingsService standings) : ControllerBase
{
    [HttpGet("{id:int}/standings")]
    public async Task<IActionResult> GetAsync(
        int id, [FromQuery] int? carClassId, CancellationToken ct) =>
        Ok(await standings.GetDriverStandingsAsync(id, carClassId, ct));

    [HttpGet("{id:int}/tt-standings")]
    public async Task<IActionResult> GetTimeTrialAsync(
        int id, [FromQuery] int? carClassId, CancellationToken ct) =>
        Ok(await standings.GetTimeTrialStandingsAsync(id, carClassId, ct));

    [HttpGet("{id:int}/qualify-results")]
    public async Task<IActionResult> GetQualifyAsync(
        int id, [FromQuery] int? carClassId, [FromQuery] int? weekNumber, CancellationToken ct) =>
        Ok(await standings.GetQualifyResultsAsync(id, carClassId, weekNumber, ct));
}
