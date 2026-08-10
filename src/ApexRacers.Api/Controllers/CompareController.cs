using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ApexRacers.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexRacers.Api.Controllers;

/// <summary>
/// Head-to-head comparison between the caller and a rival. Requires the caller to have a
/// linked iRacing account (typed 409 otherwise); the rival is identified by their cust_id.
/// </summary>
[ApiController]
[Route("api/users/me/compare")]
[Authorize]
public class CompareController(RivalComparisonService comparison, MemberContext member) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAsync([FromQuery] long rivalCustId, CancellationToken ct)
    {
        var userIdStr = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        if (rivalCustId <= 0)
            return BadRequest("A valid rivalCustId is required.");

        var custId = await member.GetRequiredCustIdAsync(userId, ct);
        return Ok(await comparison.CompareAsync(custId, rivalCustId, ct));
    }
}
