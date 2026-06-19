using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ApexRacers.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ApexRacers.Api.Controllers;

// Public — the car catalog is non-sensitive reference data. When a valid token is present the
// "your best laps" overlay on detail is populated for that user; otherwise it is omitted.
[ApiController]
[Route("api/cars")]
public class CarsController(CarCatalogService catalog) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> ListAsync(CancellationToken ct) =>
        Ok(await catalog.ListAsync(ct));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetAsync(int id, CancellationToken ct)
    {
        Guid? userId = Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out var uid)
            ? uid
            : null;

        return Ok(await catalog.GetAsync(id, userId, ct));
    }
}
