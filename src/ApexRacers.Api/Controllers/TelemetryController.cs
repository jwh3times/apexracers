using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ApexRacers.Api.Dtos;
using ApexRacers.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexRacers.Api.Controllers;

[ApiController]
[Route("api/telemetry")]
[Authorize]
public class TelemetryController(
    TelemetryUploadService uploadService,
    UploadedLapService lapService) : ControllerBase
{
    // Reads the user ID from whichever claim the JWT handler has stored it under.
    // JsonWebTokenHandler (the .NET 8+ default) does not remap "sub" → ClaimTypes.NameIdentifier,
    // so we check both to stay compatible regardless of MapInboundClaims configuration.
    private Guid? GetUserId()
    {
        var raw = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
               ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    [HttpPost("upload")]
    [RequestSizeLimit(524_288_000)] // 500 MB
    [RequestFormLimits(MultipartBodyLengthLimit = 524_288_000)]
    public async Task<IActionResult> UploadAsync(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest("No file provided.");

        if (!file.FileName.EndsWith(".ibt", StringComparison.OrdinalIgnoreCase))
            return BadRequest("File must be an iRacing .ibt telemetry file.");

        var userId = GetUserId();
        if (userId is null)
            return Unauthorized();

        try
        {
            using var stream = file.OpenReadStream();
            var result = await uploadService.ProcessAsync(stream, userId.Value, ct);

            return Ok(new TelemetryUploadResultDto(
                result.TotalLaps,
                result.ValidLaps,
                result.BestLapSeconds,
                result.TrackName,
                result.ConfigName,
                result.CarName,
                result.CustomerId,
                result.DriverName));
        }
        catch (InvalidDataException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("laps")]
    public async Task<IActionResult> GetLapsAsync(CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null)
            return Unauthorized();

        var laps = await lapService.GetUploadedBestsAsync(userId.Value, ct);
        return Ok(laps);
    }
}
