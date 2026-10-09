using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ApexRacers.Api.Dtos;
using ApexRacers.Api.Services;
using ApexRacers.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ApexRacers.Api.Controllers;

[ApiController]
[Route("api/drivers/scoped")]
[Authorize]
public sealed class ScopedDriversController(ScopedDriverPublication publication) : ControllerBase
{
    [HttpGet("personal")]
    public async Task<IActionResult> PersonalAsync(CancellationToken ct)
    {
        if (!TryUser(out var userId)) return Unauthorized();
        return await publication.ReadOwnerAsync(userId, ct);
    }
    [HttpGet("discovery")]
    [EnableRateLimiting("iracing-search")]
    public async Task<IActionResult> DiscoveryAsync([FromQuery] string? term, CancellationToken ct)
    {
        if (!TryUser(out var userId)) return Unauthorized();
        return await publication.DiscoverAsync(userId, term, ct: ct);
    }
    [HttpGet("follows")]
    public async Task<IActionResult> FollowsAsync(CancellationToken ct)
    {
        if (!TryUser(out var userId)) return Unauthorized();
        return await publication.DiscoverAsync(userId, null, followedOnly: true, ct: ct);
    }
    [HttpPost("detail")]
    public async Task<IActionResult> DetailAsync([FromBody] ScopedDriverReferenceRequest request, CancellationToken ct)
    {
        if (!TryUser(out var userId)) return Unauthorized();
        return await publication.ReadAsync(userId, request.Reference, DriverReferencePurpose.Detail, ct);
    }
    [HttpPost("comparison")]
    public async Task<IActionResult> ComparisonAsync([FromBody] ScopedDriverReferenceRequest request, CancellationToken ct)
    {
        if (!TryUser(out var userId)) return Unauthorized();
        return await publication.ReadAsync(userId, request.Reference, DriverReferencePurpose.Comparison, ct);
    }
    [HttpPost("follows")]
    public async Task<IActionResult> FollowAsync([FromBody] ScopedDriverReferenceRequest request, CancellationToken ct)
    {
        if (!TryUser(out var userId)) return Unauthorized();
        return await publication.FollowAsync(userId, request.Reference, ct);
    }
    private bool TryUser(out Guid userId) => Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out userId);
}
