using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ApexRacers.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ApexRacers.Api.Controllers;

// Public — schedule is non-sensitive. When a valid token is present the personal-best
// overlay is populated for that user; otherwise it is simply omitted.
[ApiController]
[Route("api/series")]
public class ScheduleController(ScheduleService schedule) : ControllerBase
{
    [HttpGet("{id:int}/schedule")]
    public async Task<IActionResult> GetAsync(int id, CancellationToken ct)
    {
        Guid? userId = Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out var uid)
            ? uid
            : null;

        return Ok(await schedule.GetScheduleAsync(id, userId, ct));
    }
}
