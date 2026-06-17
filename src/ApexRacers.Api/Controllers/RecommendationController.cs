using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexRacers.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/users/me/recommendations")]
public class RecommendationsController(
    CarRecommendationService recommendations,
    MemberContext member) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetRecommendationsAsync(
        [FromQuery] int seriesId,
        [FromQuery] int weekNumber,
        [FromQuery] bool includePersonalLaps = false,
        [FromQuery] List<LapSessionType>? personalLapTypes = null,
        CancellationToken ct = default)
    {
        var userIdStr = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var custId = await member.GetCustIdAsync(userId, ct);
        if (custId is null or 0)
            return this.IRacingNotLinked();

        return Ok(await recommendations.GetRecommendationsAsync(
            seriesId, weekNumber, custId.Value, includePersonalLaps, personalLapTypes, ct));
    }
}
