using ApexRacers.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ApexRacers.Api.Controllers;

[ApiController]
[Route("api/users/me/recommendations")]
public class RecommendationsController(CarRecommendationService recommendations) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetRecommendationsAsync([FromQuery] int weekId, CancellationToken ct)
    {
        // TODO: Replace hardcoded 0 with IRacingCustomerId extracted from authenticated user's claims
        const long customerId = 0;
        return Ok(await recommendations.GetRecommendationsAsync(weekId, customerId, ct));
    }
}
