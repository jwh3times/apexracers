using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ApexRacers.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexRacers.Api.Controllers;

[ApiController]
[Route("api/users/me/profile-stats")]
[Authorize]
public class ProfileStatsController(
    DriverStatsService stats,
    SubjectDriverContext subjectDriverContext) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAsync(CancellationToken ct)
    {
        var userIdStr = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var subjectDriverCustId = await subjectDriverContext
            .GetRequiredSubjectDriverCustIdAsync(userId, ct);
        return Ok(await stats.GetDriverProfileAsync(subjectDriverCustId, ct));
    }
}
