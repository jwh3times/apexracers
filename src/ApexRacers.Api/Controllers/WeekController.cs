using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ApexRacers.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexRacers.Api.Controllers;

// Public week data, plus one Authorize action for the caller's own percentiles
// (mirrors SubsessionController's public detail + Authorize lap-trace split).
[ApiController]
[Route("api/series/{seriesId}/weeks/{weekNumber}")]
public class WeekController(
    WeekCarStatsService weekCarStats,
    CarRecommendationService recommendations,
    MemberContext member) : ControllerBase
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

    [HttpGet("my-percentiles")]
    [Authorize]
    public async Task<IActionResult> GetMyPercentilesAsync(int seriesId, int weekNumber, CancellationToken ct)
    {
        var userIdStr = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var custId = await member.GetCustIdAsync(userId, ct);
        if (custId is null or 0)
            return this.IRacingNotLinked();

        return Ok(await recommendations.GetMyPercentilesAsync(seriesId, weekNumber, custId.Value, ct));
    }
}
