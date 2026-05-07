using ApexRacers.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ApexRacers.Api.Controllers;

[ApiController]
[Route("api/series")]
public class SeriesController(SeriesService series) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetSeriesAsync(CancellationToken ct)
        => Ok(await series.GetActiveSeriesAsync(ct));
}
