using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexRacers.Api.Controllers;

[ApiController]
[Route("api/users/me/analytics")]
[Authorize]
public class UserAnalyticsController(UserAnalyticsService analytics) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAnalyticsAsync(
        [FromQuery] int? seriesId,
        [FromQuery] bool includePersonalLaps = false,
        [FromQuery] List<LapSessionType>? personalLapTypes = null,
        CancellationToken ct = default)
    {
        var userIdStr = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var evidence = PersonalBestEvidence.FromRequest(includePersonalLaps, personalLapTypes);
        return Ok(await analytics.GetAnalyticsAsync(userId, seriesId, evidence, ct));
    }
}
