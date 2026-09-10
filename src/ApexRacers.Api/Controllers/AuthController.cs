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
public class AuthController(AuthService auth) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> RegisterAsync([FromBody] RegisterRequest request, CancellationToken ct)
    {
        await auth.RegisterAsync(request, ct);

        // Byte-identical whether the address was free or already taken, and it carries no token —
        // registration used to answer "Email '…' is already taken." verbatim, which let anyone test
        // who has an account (GHSA-72v6-mw4c-q96r). The confirmation link leaves the server only
        // inside the email, and the account cannot sign in until it is followed, so neither this
        // response nor a follow-up sign-in attempt distinguishes the two cases.
        return Ok(new MessageResponse(
            "If that address can be registered, a confirmation link has been sent to it. " +
            "Confirm your email to sign in."));
    }

    [HttpPost("confirm-email")]
    public async Task<IActionResult> ConfirmEmailAsync([FromBody] ConfirmEmailRequest request, CancellationToken ct)
    {
        await auth.ConfirmEmailAsync(request.UserId, request.Token, ct);
        return NoContent();
    }

    [HttpPost("login")]
    public async Task<IActionResult> LoginAsync([FromBody] LoginRequest request, CancellationToken ct)
    {
        var auth401 = await auth.LoginAsync(request, ct);

        // One refusal for every way a sign-in can fail. This used to answer 423 for a locked account,
        // which only a real one can be — five wrong passwords against a registered address returned
        // 423 while an unregistered one returned 401 forever, so the pair enumerated accounts
        // (GHSA-28pc-cx5w-g6jp). The lockout now reaches its owner by email instead.
        //
        // Carries an explicit Detail rather than a bare Unauthorized(): the client renders
        // ProblemDetails.detail, and an automatic ProblemDetails has none.
        return auth401 is null
            ? Problem(
                detail: "Invalid email or password.",
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized")
            : Ok(auth401);
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
        await auth.RequestPasswordResetAsync(request.Email, ct);

        // The response is identical in every environment and whether or not the account exists —
        // it carries no token. The reset token is a single-use credential and leaves the server
        // only inside the emailed link; a Development stack reads it back from the mail drop
        // directory (DEV_MAIL_DROP_PATH), which has no HTTP surface. Echoing it here made any
        // reachable Development instance an account-takeover path (GHSA-qmqp-gxpr-867g).
        return Ok(new ForgotPasswordResponse(
            "If an account exists for that email, a password reset link has been sent."));
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

        await auth.RequestEmailChangeAsync(userId, request.NewEmail, request.CurrentPassword, ct);
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
