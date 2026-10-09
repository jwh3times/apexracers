using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ApexRacers.Api.Dtos;
using ApexRacers.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexRacers.Api.Controllers;

[ApiController, Route("api/drivers/privacy"), Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DriverPrivacyController(DriverPrivacy privacy) : ControllerBase
{
    [HttpPost("withdrawal")]
    public async Task<IActionResult> WithdrawAsync(DriverWithdrawalRequest request, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId)) return Unauthorized();
        return await privacy.WithdrawAsync(userId, request, ct);
    }
}
