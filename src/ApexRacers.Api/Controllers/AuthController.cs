using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ApexRacers.Api.Dtos;
using ApexRacers.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ApexRacers.Api.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class AuthController(AuthService auth, IWebHostEnvironment env) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> RegisterAsync([FromBody] RegisterRequest request, CancellationToken ct) =>
        Ok(await auth.RegisterAsync(request, ct));

    [HttpPost("login")]
    public async Task<IActionResult> LoginAsync([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await auth.LoginAsync(request, ct);
        if (result.LockedOut)
            return StatusCode(StatusCodes.Status423Locked,
                "Account temporarily locked due to repeated failed sign-in attempts. Try again later.");
        // Carries an explicit Detail rather than a bare Unauthorized(): the client renders
        // ProblemDetails.detail, and an automatic ProblemDetails has none. Deliberately
        // ambiguous about which half was wrong, so this cannot be used to enumerate accounts.
        return result.Auth is null
            ? Problem(
                detail: "Invalid email or password.",
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized")
            : Ok(result.Auth);
    }

    [HttpPut("profile")]
    [Authorize]
    public async Task<IActionResult> UpdateProfileAsync([FromBody] UpdateProfileRequest request, CancellationToken ct)
    {
        var userIdStr = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        return Ok(await auth.UpdateProfileAsync(userId, request, ct));
    }

    [HttpPut("role")]
    [Authorize]
    public async Task<IActionResult> UpdateRoleAsync([FromBody] UpdateRoleRequest request, CancellationToken ct)
    {
        var userIdStr = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        return Ok(await auth.UpdateRoleAsync(userId, request.Role, ct));
    }

    [HttpPut("theme")]
    [Authorize]
    public async Task<IActionResult> UpdateThemeAsync([FromBody] UpdateThemeRequest request, CancellationToken ct)
    {
        var userIdStr = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        return Ok(await auth.UpdateThemeAsync(userId, request.ThemePreference, ct));
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> RefreshAsync([FromBody] RefreshRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await auth.RefreshAsync(request.RefreshToken, ct));
        }
        catch (InvalidOperationException)
        {
            return Unauthorized();
        }
    }

    [HttpPost("logout")]
    public async Task<IActionResult> LogoutAsync([FromBody] RevokeRequest request, CancellationToken ct)
    {
        await auth.RevokeAsync(request.RefreshToken, ct);
        return NoContent();
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePasswordAsync([FromBody] ChangePasswordRequest request, CancellationToken ct)
    {
        var userIdStr = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        await auth.ChangePasswordAsync(userId, request, ct);
        return NoContent();
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPasswordAsync([FromBody] ForgotPasswordRequest request, CancellationToken ct)
    {
        var token = await auth.RequestPasswordResetAsync(request.Email, ct);

        // In Development the token is also returned in the response body so the reset flow is
        // testable end-to-end without inspecting an inbox; in every other environment it is
        // withheld and the response is identical whether or not the account exists. The token
        // is deliberately never logged — it is a single-use credential.
        return Ok(new ForgotPasswordResponse(
            "If an account exists for that email, a password reset link has been sent.",
            env.IsDevelopment() ? token : null));
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPasswordAsync([FromBody] ResetPasswordRequest request, CancellationToken ct)
    {
        await auth.ResetPasswordAsync(request, ct);
        return NoContent();
    }

    [HttpPost("request-email-change")]
    [Authorize]
    public async Task<IActionResult> RequestEmailChangeAsync([FromBody] RequestEmailChangeRequest request, CancellationToken ct)
    {
        var userIdStr = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        await auth.RequestEmailChangeAsync(userId, request.NewEmail, ct);
        // Generic response — never reveals whether the target address is already in use.
        return Ok(new MessageResponse("If that address is available, a confirmation email has been sent."));
    }

    [HttpPost("confirm-email-change")]
    public async Task<IActionResult> ConfirmEmailChangeAsync([FromBody] ConfirmEmailChangeRequest request, CancellationToken ct)
    {
        await auth.ConfirmEmailChangeAsync(request.UserId, request.NewEmail, request.Token, ct);
        return NoContent();
    }

    [HttpPost("callback")]
    [Authorize]
    public async Task<IActionResult> CallbackAsync(
        [FromQuery] string? code,
        [FromQuery] string? state,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
            return BadRequest("code and state are required.");

        try
        {
            return Ok(await auth.HandleCallbackAsync(code, state, ct));
        }
        catch (NotImplementedException)
        {
            return StatusCode(501, "iRacing OAuth linking is not yet available.");
        }
    }
}
