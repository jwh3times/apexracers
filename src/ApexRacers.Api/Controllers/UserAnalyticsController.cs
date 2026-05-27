using System.Security.Claims;
using ApexRacers.Api.Services;
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
        CancellationToken ct)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        return Ok(await analytics.GetAnalyticsAsync(userId, seriesId, ct));
    }
}
