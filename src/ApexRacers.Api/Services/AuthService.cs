using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ApexRacers.Api.Dtos;
using ApexRacers.Api.Services.Email;
using ApexRacers.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

namespace ApexRacers.Api.Services;

public class AuthService(
    UserManager<ApplicationUser> userManager,
    IConfiguration config,
    JwtSettings jwt,
    RefreshTokenStore refreshTokens,
    IEmailSender emailSender)
{
    private const int AccessTokenMinutes = 15;

    private static readonly string[] SelfAssignableRoles = ["Beta", "Alpha"];

    private string BaseUrl => config["APP_BASE_URL"]?.TrimEnd('/') ?? "https://apexracers.gg";

    public async Task<AuthResultDto> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var user = new ApplicationUser
        {
            Id          = Guid.NewGuid(),
            UserName    = request.Email,
            Email       = request.Email,
            DisplayName = request.Email.Split('@')[0],
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            // Identity's error descriptions are user-facing by design (e.g. "Passwords must
            // have at least one digit."); surface them so the caller knows what to fix.
            throw new InvalidOperationException(
                string.Join(" ", result.Errors.Select(e => e.Description)));

        await userManager.AddToRoleAsync(user, "Standard");

        var jwt     = await GenerateJwtAsync(user);
        var refresh = await refreshTokens.IssueAsync(user.Id, ct);
        return new AuthResultDto(jwt, user.Id, user.DisplayName, refresh);
    }

    public async Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
            return LoginResult.Invalid;

        // A locked-out account is denied before the password is even checked, so a
        // correct guess during the lockout window still fails.
        if (await userManager.IsLockedOutAsync(user))
            return LoginResult.Locked;

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            // UserManager.CheckPasswordAsync does not track failures the way SignInManager
            // does, so increment the counter manually; it locks the account once the
            // configured MaxFailedAccessAttempts threshold is reached.
            await userManager.AccessFailedAsync(user);
            return await userManager.IsLockedOutAsync(user) ? LoginResult.Locked : LoginResult.Invalid;
        }

        await userManager.ResetAccessFailedCountAsync(user);

        var jwt     = await GenerateJwtAsync(user);
        var refresh = await refreshTokens.IssueAsync(user.Id, ct);
        return LoginResult.Success(new AuthResultDto(jwt, user.Id, user.DisplayName, refresh));
    }

    public async Task<AuthResultDto> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName))
            throw new InvalidOperationException("Display name cannot be empty.");

        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException("User not found.");

        user.DisplayName = request.DisplayName.Trim();
        if (request.IRacingCustomerId.HasValue)
            user.IRacingCustomerId = request.IRacingCustomerId.Value;
        if (!string.IsNullOrWhiteSpace(request.ThemePreference) &&
            request.ThemePreference is "auto" or "light" or "dark")
            user.ThemePreference = request.ThemePreference;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
            throw new InvalidOperationException("Update failed. Please try again.");

        return new AuthResultDto(await GenerateJwtAsync(user), user.Id, user.DisplayName);
    }

    public async Task<AuthResultDto> UpdateRoleAsync(Guid userId, string newRole, CancellationToken ct = default)
    {
        if (!SelfAssignableRoles.Contains(newRole, StringComparer.OrdinalIgnoreCase) &&
            !string.Equals(newRole, "Standard", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Role must be Standard, Beta, or Alpha.");

        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException("User not found.");

        var currentRoles = await userManager.GetRolesAsync(user);

        if (currentRoles.Contains("Admin"))
            throw new InvalidOperationException("Admin role cannot be changed via self-service.");

        await userManager.RemoveFromRolesAsync(user, currentRoles);
        await userManager.AddToRoleAsync(user, newRole);

        return new AuthResultDto(await GenerateJwtAsync(user), user.Id, user.DisplayName);
    }

    public async Task<AuthResultDto> UpdateThemeAsync(Guid userId, string themePreference, CancellationToken ct = default)
    {
        if (themePreference is not ("auto" or "light" or "dark"))
            throw new InvalidOperationException("Theme must be auto, light, or dark.");

        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException("User not found.");

        user.ThemePreference = themePreference;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
            throw new InvalidOperationException("Update failed. Please try again.");

        return new AuthResultDto(await GenerateJwtAsync(user), user.Id, user.DisplayName);
    }

    public async Task<AuthResultDto> RefreshAsync(string rawToken, CancellationToken ct = default)
    {
        var rotation = await refreshTokens.RotateAsync(rawToken, ct);
        var user = await userManager.FindByIdAsync(rotation.UserId.ToString())
            ?? throw new InvalidOperationException("User not found.");

        var jwt = await GenerateJwtAsync(user);
        return new AuthResultDto(jwt, user.Id, user.DisplayName, rotation.RawToken);
    }

    public Task RevokeAsync(string rawToken, CancellationToken ct = default) =>
        refreshTokens.RevokeAsync(rawToken, ct);

    /// <summary>
    /// Deletes refresh tokens whose expiry is older than <paramref name="retention"/>.
    /// Revoked and naturally-expired rows otherwise accumulate forever; this is invoked
    /// once at API startup. Uses a tracked delete so it works on every EF provider.
    /// </summary>
    public Task<int> PurgeExpiredRefreshTokensAsync(
        TimeSpan retention,
        CancellationToken ct = default) =>
        refreshTokens.PurgeExpiredAsync(retention, ct);

    /// <summary>
    /// Changes the password for an authenticated user who supplies their current password.
    /// </summary>
    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException("User not found.");

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
            // Identity's descriptions cover both "incorrect password" and policy failures
            // (e.g. "Passwords must be at least 8 characters."); surface them to the caller.
            throw new InvalidOperationException(
                string.Join(" ", result.Errors.Select(e => e.Description)));
    }

    /// <summary>
    /// Generates a single-use reset token for the account and emails the reset link. Returns the token
    /// (for Development-only echoing) or null when no account exists for the email.
    /// </summary>
    public async Task<string?> RequestPasswordResetAsync(string email, CancellationToken ct = default)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
            return null;

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var url = $"{BaseUrl}/reset-password?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
        await emailSender.SendAsync(AccountEmailTemplates.PasswordReset(email, url), ct);
        return token;
    }

    /// <summary>
    /// Begins a verify-then-apply email change: emails a confirmation link to the new address. The account
    /// email is unchanged until <see cref="ConfirmEmailChangeAsync"/> runs. Enumeration-safe — if the target
    /// address already belongs to another account, nothing is sent.
    /// </summary>
    public async Task RequestEmailChangeAsync(Guid userId, string newEmail, CancellationToken ct = default)
    {
        newEmail = newEmail?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(newEmail))
            throw new InvalidOperationException("Email address cannot be empty.");

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return;

        var existing = await userManager.FindByEmailAsync(newEmail);
        if (existing is not null && existing.Id != userId)
            return;

        var token = await userManager.GenerateChangeEmailTokenAsync(user, newEmail);
        var url = $"{BaseUrl}/verify-email?userId={userId}&email={Uri.EscapeDataString(newEmail)}&token={Uri.EscapeDataString(token)}";
        await emailSender.SendAsync(AccountEmailTemplates.EmailChangeVerification(newEmail, url), ct);

        // Security notice to the current (old) address: in a hijacked-session takeover the verification
        // link goes to the attacker's inbox, so this is the real owner's earliest chance to react.
        if (!string.IsNullOrEmpty(user.Email))
            await emailSender.SendAsync(
                AccountEmailTemplates.EmailChangeNotice(user.Email, newEmail, $"{BaseUrl}/forgot-password"), ct);
    }

    /// <summary>
    /// Resets a password using a token from <see cref="RequestPasswordResetAsync"/>.
    /// Because a reset is an account-recovery action, every outstanding refresh token is
    /// revoked so any session opened before the reset is cut off.
    /// </summary>
    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default)
    {
        var user = await userManager.FindByEmailAsync(request.Email)
            ?? throw new InvalidOperationException("Invalid or expired password reset request.");

        var result = await userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                string.Join(" ", result.Errors.Select(e => e.Description)));

        await refreshTokens.RevokeAllActiveAsync(user.Id, ct);
    }

    /// <summary>
    /// Applies a pending email change using a token from <see cref="RequestEmailChangeAsync"/>. Keeps the
    /// username in sync (login is by email) and revokes all active refresh tokens (account-recovery action).
    /// </summary>
    public async Task ConfirmEmailChangeAsync(Guid userId, string newEmail, string token, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException("Invalid or expired email change request.");

        var trimmed = newEmail.Trim();
        var result = await userManager.ChangeEmailAsync(user, trimmed, token);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(e => e.Description)));

        await userManager.SetUserNameAsync(user, trimmed);
        await refreshTokens.RevokeAllActiveAsync(user.Id, ct);
    }

    // TODO: Validate state against a nonce store to prevent CSRF; exchange the authorization
    //       code for an iRacing access token via the Authorization Code flow; fetch driver
    //       profile (customerId, displayName) from iRacing; update ApplicationUser.IRacingCustomerId;
    //       re-issue JWT with updated claims
    public Task<AuthResultDto> HandleCallbackAsync(string code, string state, CancellationToken ct = default)
        => throw new NotImplementedException();

    // ── Helpers ───────────────────────────────────────────────────────────────

    internal async Task<string> GenerateJwtAsync(ApplicationUser user)
    {
        var creds = new SigningCredentials(jwt.SecurityKey(), SecurityAlgorithms.HmacSha256);

        var roles = await userManager.GetRolesAsync(user);
        var role  = roles.FirstOrDefault() ?? "Standard";

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub,   user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email!),
            new(JwtRegisteredClaimNames.Name,  user.DisplayName),
            new("role", role),
        };
        if (user.IRacingCustomerId.HasValue)
            claims.Add(new Claim("iracing_id", user.IRacingCustomerId.Value.ToString()));
        claims.Add(new Claim("theme_preference", user.ThemePreference));

        var token = new JwtSecurityToken(
            issuer:             jwt.Issuer,
            audience:           jwt.Audience,
            claims:             claims,
            expires:            DateTime.UtcNow.AddMinutes(AccessTokenMinutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

/// <summary>
/// Outcome of a login attempt. <see cref="Auth"/> is non-null only on success;
/// <see cref="LockedOut"/> distinguishes a throttled account from bad credentials.
/// </summary>
public record LoginResult(AuthResultDto? Auth, bool LockedOut)
{
    public static LoginResult Success(AuthResultDto auth) => new(auth, false);
    public static readonly LoginResult Invalid = new(null, false);
    public static readonly LoginResult Locked  = new(null, true);
}
