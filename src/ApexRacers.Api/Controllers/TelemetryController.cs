using ApexRacers.Api.Dtos;
using ApexRacers.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ApexRacers.Api.Controllers;

[ApiController]
[Route("api/telemetry")]
public class TelemetryController(
    TelemetryUploadService uploadService,
    PersonalLapService lapService) : ControllerBase
{
    [HttpPost("upload")]
    [RequestSizeLimit(524_288_000)] // 500 MB
    [RequestFormLimits(MultipartBodyLengthLimit = 524_288_000)]
    public async Task<IActionResult> UploadAsync(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest("No file provided.");

        if (!file.FileName.EndsWith(".ibt", StringComparison.OrdinalIgnoreCase))
            return BadRequest("File must be an iRacing .ibt telemetry file.");

        try
        {
            using var stream = file.OpenReadStream();
            var result = await uploadService.ProcessAsync(stream, ct);

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
    public async Task<IActionResult> GetLapsAsync([FromQuery] long customerId, CancellationToken ct)
    {
        if (customerId <= 0) return BadRequest("customerId is required.");
        var laps = await lapService.GetPersonalBestsAsync(customerId, ct);
        return Ok(laps);
    }
}
