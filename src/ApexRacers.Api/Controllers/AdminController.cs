using ApexRacers.Api.Dtos;
using ApexRacers.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexRacers.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Policy = "AdminOnly")]
public class AdminController(AdminService admin) : ControllerBase
{
    // ── Users ─────────────────────────────────────────────────────────────────

    [HttpGet("users")]
    public async Task<IActionResult> GetUsersAsync(CancellationToken ct) =>
        Ok(await admin.GetUsersAsync(ct));

    [HttpPut("users/{userId:guid}/role")]
    public async Task<IActionResult> SetUserRoleAsync(Guid userId, [FromBody] AdminUpdateUserRoleRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await admin.SetUserRoleAsync(userId, request.Role, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    // ── Feature flags ─────────────────────────────────────────────────────────

    [HttpGet("feature-flags")]
    public async Task<IActionResult> GetFlagsAsync(CancellationToken ct) =>
        Ok(await admin.GetAllFlagsAsync(ct));

    [HttpPost("feature-flags")]
    public async Task<IActionResult> CreateFlagAsync([FromBody] CreateFeatureFlagRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await admin.CreateFlagAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("feature-flags/{id:int}")]
    public async Task<IActionResult> UpdateFlagAsync(int id, [FromBody] UpdateFeatureFlagRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await admin.UpdateFlagAsync(id, request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("feature-flags/{id:int}")]
    public async Task<IActionResult> DeleteFlagAsync(int id, CancellationToken ct)
    {
        try
        {
            await admin.DeleteFlagAsync(id, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ex.Message);
        }
    }
}
