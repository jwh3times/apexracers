using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ApexRacers.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexRacers.Api.Controllers;

[ApiController]
[Route("api/subsessions")]
public class SubsessionController(
    SubsessionDetailService detail,
    LapDataService lapData,
    MemberContext member) : ControllerBase
{
    // Public — official race results, mirroring WeekController. Unknown ids surface as a
    // 404 via the KeyNotFoundException mapping in ExceptionHandlingMiddleware.
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetAsync(int id, CancellationToken ct) =>
        Ok(await detail.GetAsync(id, ct));

    // Per-driver lap trace. Defaults to the caller's own laps when customerId is omitted.
    [HttpGet("{id:int}/laps")]
    [Authorize]
    public async Task<IActionResult> GetLapsAsync(
        int id, [FromQuery] long? customerId, CancellationToken ct)
    {
        var custId = customerId;
        if (custId is null)
        {
            var userIdStr = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            if (!Guid.TryParse(userIdStr, out var userId))
                return Unauthorized();
            custId = await member.GetCustIdAsync(userId, ct);
        }

        if (custId is null or 0)
            return this.IRacingNotLinked();

        return Ok(await lapData.GetDriverLapsAsync(id, custId.Value, ct));
    }
}
