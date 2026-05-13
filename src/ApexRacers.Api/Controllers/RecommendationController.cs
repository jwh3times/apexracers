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
        var customerIdClaim = User.FindFirst("iracing_id")?.Value;
        if (!long.TryParse(customerIdClaim, out var customerId))
            return Ok(Array.Empty<object>());

        return Ok(await recommendations.GetRecommendationsAsync(weekId, customerId, ct));
    }
}
