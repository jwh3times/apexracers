using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ApexRacers.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexRacers.Api.Controllers;

[ApiController]
[Route("api/users/me/progression")]
[Authorize]
public class ProgressionController(MemberStatsService stats, MemberContext member) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAsync(CancellationToken ct)
    {
        var userIdStr = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var custId = await member.GetCustIdAsync(userId, ct);
        if (custId is null or 0)
            return this.IRacingNotLinked();

        return Ok(await stats.GetProgressionAsync(custId.Value, ct));
    }
}
