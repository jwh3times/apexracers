using ApexRacers.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ApexRacers.Api.Controllers;

// Public — the race guide is non-sensitive, live schedule data.
[ApiController]
[Route("api/race-guide")]
public class RaceGuideController(RaceGuideService raceGuide) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAsync(CancellationToken ct) =>
        Ok(await raceGuide.GetGuideAsync(ct));
}
