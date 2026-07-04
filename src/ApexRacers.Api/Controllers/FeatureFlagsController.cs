using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ApexRacers.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexRacers.Api.Controllers;

[ApiController]
[Route("api/feature-flags")]
public class FeatureFlagsController(AdminService admin) : ControllerBase
{
    /// <summary>
    /// Authenticated callers get their role-eligible flag set; anonymous callers get
    /// the public set (enabled flags open to Standard). Public so that guests see
    /// flag-gated public pages — e.g. /series once iracing-live is enabled (GA).
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAsync(CancellationToken ct)
    {
        var userIdStr = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (Guid.TryParse(userIdStr, out var userId))
            return Ok(await admin.GetFlagsForUserAsync(userId, ct));

        return Ok(await admin.GetFlagsForRoleAsync("Standard", ct));
    }
}
