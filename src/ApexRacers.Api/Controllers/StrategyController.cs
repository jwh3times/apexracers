using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ApexRacers.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ApexRacers.Api.Controllers;

// Public — BoP/weather/track strategy context is non-sensitive. When a valid token is present the
// personalized "optimal for you" overlay (per-car percentile, projected lap, rank) is included;
// otherwise it is simply omitted.
[ApiController]
[Route("api/series/{seriesId:int}/weeks/{raceWeekIndex:int}/strategy")]
public class StrategyController(StrategyService strategy) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAsync(int seriesId, int raceWeekIndex, CancellationToken ct)
    {
        Guid? userId = Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out var uid)
            ? uid
            : null;

        return Ok(await strategy.GetStrategyAsync(seriesId, raceWeekIndex, userId, ct));
    }
}
