using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexRacers.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/series/{seriesId}/weeks/{weekNumber}/cars/{carId}/percentile")]
public class PercentileController(PercentileCalculationService percentile) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetPercentileAsync(
        int seriesId,
        int weekNumber,
        int carId,
        [FromQuery] long customerId,
        [FromQuery] bool includeUploadedLaps = false,
        [FromQuery] List<LapSessionType>? uploadedLapTypes = null,
        CancellationToken ct = default)
    {
        Guid? callerUserId = Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out var g) ? g : null;
        var evidence = PersonalBestEvidence.FromRequest(includeUploadedLaps, uploadedLapTypes);
        var result = await percentile.ComputeAndCacheAsync(
            seriesId, weekNumber, carId, customerId, evidence, callerUserId, ct);
        return result is null ? NotFound() : Ok(result);
    }
}
