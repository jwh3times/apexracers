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
}
