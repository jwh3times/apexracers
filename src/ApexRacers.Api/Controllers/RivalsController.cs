using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ApexRacers.Api.Dtos;
using ApexRacers.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexRacers.Api.Controllers;

/// <summary>
/// The rivals a user follows for head-to-head comparison: list/add/remove, driver
/// name-search, and suggestions drawn from drivers the caller has actually raced.
/// </summary>
[ApiController]
[Route("api/users/me/rivals")]
[Authorize]
public class RivalsController(RivalService rivals, MemberContext member) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> ListAsync(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        return Ok(await rivals.ListAsync(userId, ct));
    }

    [HttpPost]
    public async Task<IActionResult> AddAsync([FromBody] AddRivalRequest body, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        return Ok(await rivals.AddAsync(userId, body.CustId, body.DisplayName ?? string.Empty, ct));
    }

    [HttpDelete("{custId:long}")]
    public async Task<IActionResult> RemoveAsync(long custId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        await rivals.RemoveAsync(userId, custId, ct);
        return NoContent();
    }

    [HttpGet("search")]
    public async Task<IActionResult> SearchAsync([FromQuery] string term, CancellationToken ct)
    {
        if (!TryGetUserId(out _)) return Unauthorized();
        return Ok(await rivals.SearchDriversAsync(term ?? string.Empty, ct));
    }

    [HttpGet("suggestions")]
    public async Task<IActionResult> SuggestionsAsync(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var custId = await member.GetRequiredCustIdAsync(userId, ct);
        return Ok(await rivals.SuggestionsAsync(userId, custId, ct));
    }

    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out userId);
}
