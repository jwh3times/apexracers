using ApexRacers.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexRacers.Api.Controllers;

[ApiController]
[Route("api/leaderboards")]
[Authorize]
public class LeaderboardController(LeaderboardService leaderboard) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAsync([FromQuery] int categoryId = 5, CancellationToken ct = default)
    {
        if (categoryId is < 1 or > 6)
            throw new ArgumentException("categoryId must be between 1 and 6.");

        return Ok(await leaderboard.GetLeaderboardAsync(categoryId, ct));
    }
}
