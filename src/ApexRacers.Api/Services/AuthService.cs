using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ApexRacers.Api.Dtos;
using ApexRacers.Api.Services.Email;
using ApexRacers.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ApexRacers.Api.Services;

/// <summary>
/// A successful sign-in: the body the caller receives, plus the known-device cookie the response
/// should carry (issue #314).
/// </summary>
/// <remarks>
/// The device value is deliberately <em>not</em> a field on <see cref="AuthResultDto"/>. That DTO is
/// serialised into the response body, where page script could read it — and a device a script can
/// read is a device an injected script can steal, which is the whole reason the cookie is
/// <c>HttpOnly</c>. Keeping it on this type, which is never serialised, means the only way it can
/// reach a client is as a cookie the controller sets.
/// </remarks>
/// <param name="Result">The response body.</param>
/// <param name="Device">
/// The cookie value and expiry to set, or <c>null</c> when the browser could not be remembered —
/// a lost exemption, never a failed sign-in.
/// </param>
public sealed record SignInOutcome(AuthResultDto Result, IssuedDevice? Device);

public class AuthService(
    UserManager<ApplicationUser> userManager,
    IConfiguration config,
    JwtSettings jwt,
    RefreshTokenStore refreshTokens,
    IEmailSender emailSender,
    SignInProtection signInProtection,
    KnownDeviceStore knownDevices,
    DriverAuthorityStore driverAuthority)
{
    private const int AccessTokenMinutes = 15;

    private static readonly string[] SelfAssignableRoles = ["Beta", "Alpha"];

    private string BaseUrl => config["APP_BASE_URL"]?.TrimEnd('/') ?? "https://apexracers.gg";

    /// <summary>
    /// Creates an account and emails a confirmation link. Deliberately returns nothing: the caller is
    /// never told whether the address was free, so registration cannot be used to test who has an
    /// account (GHSA-72v6-mw4c-q96r). The account is unusable until <see cref="ConfirmEmailAsync"/>
    /// runs, which is what closes the oracle — see the remarks.
    /// </summary>
    /// <remarks>
    /// A generic response alone would not have been enough. If registering a free address produced a
    /// usable account while registering a taken one did not, an attacker could simply sign in with the
    /// password they just submitted and read the answer off that. Requiring confirmation before sign-in
    /// makes both outcomes indistinguishable end to end: unknown, taken, and just-created addresses all
    /// return this same acknowledgement and all fail to sign in.
    /// </remarks>
    public async Task RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var email = request.Email?.Trim() ?? string.Empty;
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            DisplayName = email.Split('@')[0],
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(user, "Standard");
            await SendEmailConfirmationAsync(user, ct);
            return;
        }

        // UserManager.CreateAsync validates the password before it touches the store and returns on
        // the first failure, so a policy rejection is reached identically for a free and for a taken
        // address. Those descriptions are user-facing by design ("Passwords must have at least one
        // digit.") and reveal nothing about who is registered, so they still surface.
        var disclosable = result.Errors.Where(e => !IsDuplicateAccount(e.Code)).ToList();
        if (disclosable.Count > 0)
            throw new InvalidOperationException(
                string.Join(" ", disclosable.Select(e => e.Description)));

        // Only duplicate-account errors are left, so the address is taken. Say nothing to the caller
        // and tell the mailbox owner instead — they are the one entitled to know.
        await NotifyAddressAlreadyRegisteredAsync(email, ct);
    }

    /// <summary>
    /// Confirms a newly registered account from the emailed link, making sign-in possible. Idempotent:
    /// a second click on the same link succeeds rather than reporting a failure.
    /// </summary>
    public async Task ConfirmEmailAsync(Guid userId, string token, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException("Invalid or expired email confirmation link.");

        if (await userManager.IsEmailConfirmedAsync(user))
            return;

        var result = await userManager.ConfirmEmailAsync(user, token);
        if (!result.Succeeded)
            // Identity's descriptions here ("Invalid token.") add nothing the user can act on, and
            // the failure modes are not worth distinguishing to an unauthenticated caller.
            throw new InvalidOperationException("Invalid or expired email confirmation link.");
    }

    /// <summary>
    /// Issues credentials for a protected sign-in, or returns the same null for every refusal.
    /// <see cref="SignInProtection"/> owns recognition, admission, password work and cleanup.
    /// </summary>
    public async Task<SignInOutcome?> SignInAsync(
        LoginRequest request,
        string? sourceAddress,
        string? deviceToken,
        CancellationToken ct = default)
    {
        var authenticated = await signInProtection.AuthenticateAsync(request, sourceAddress, deviceToken, ct);
        if (authenticated is null)
            return null;

        var user = authenticated.User;
        var token = await GenerateJwtAsync(user);
        var refresh = await refreshTokens.IssueAsync(user.Id, ct);
        return new SignInOutcome(
            new AuthResultDto(token, user.Id, user.DisplayName, refresh),
            authenticated.Device);
    }

    /// <summary>
    /// Sign-in without the known-device cookie — the ordinary address-scoped path, and what tooling
    /// and tests use. See <see cref="SignInAsync"/> for the full contract.
    /// </summary>
    public async Task<AuthResultDto?> LoginAsync(
        LoginRequest request,
        string? sourceAddress = null,
        CancellationToken ct = default) =>
        (await SignInAsync(request, sourceAddress, null, ct))?.Result;

    /// <summary>
    /// Sign-in with a presented known-device cookie, discarding the cookie the outcome would set.
    /// For tests that exercise the exemption without needing the response side.
    /// </summary>
    public async Task<AuthResultDto?> LoginAsync(
        LoginRequest request,
        string? sourceAddress,
        string? deviceToken,
        CancellationToken ct = default) =>
        (await SignInAsync(request, sourceAddress, deviceToken, ct))?.Result;

    public async Task<AuthResultDto> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName))
            throw new InvalidOperationException("Display name cannot be empty.");

        ApplicationUser user;
        try
        {
            user = await driverAuthority.UpdateUnverifiedProfileAsync(userId, async (current, associated, token) =>
            {
                if (request.IRacingCustomerId.HasValue && request.IRacingCustomerId != current.IRacingCustomerId)
                {
                    await RequireCurrentPasswordAsync(current, request.CurrentPassword);
                    // Grant creation cannot race this check or the subsequent Identity save.
                    // A profile edit cannot stand in for journal-first unlink/reassignment.
                    if (associated) throw new IRacingNotConfiguredException();
                }
                current.DisplayName = request.DisplayName.Trim();
                if (request.IRacingCustomerId.HasValue)
                    current.IRacingCustomerId = request.IRacingCustomerId.Value;
                if (!string.IsNullOrWhiteSpace(request.ThemePreference) &&
                    request.ThemePreference is "auto" or "light" or "dark")
                    current.ThemePreference = request.ThemePreference;
                token.ThrowIfCancellationRequested();
                var result = await userManager.UpdateAsync(current);
                if (!result.Succeeded)
                    throw new InvalidOperationException("Update failed. Please try again.");
                return current;
            }, ct);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "IX_Users_IRacingCustomerId",
            })
        {
            throw new ClaimedIdentityConflictException(ex);
        }

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

        await refreshTokens.RevokeAllActiveAsync(user.Id, ct);
        // Devices are forgotten alongside the sessions. Changing or resetting a password is
        // the documented remedy for a machine the Driver no longer trusts, and a device record
        // outliving it would leave its holder a standing guessing allowance against the new
        // password — see KnownDeviceStore.ForgetAllAsync.
        await knownDevices.ForgetAllAsync(user.Id, ct);
    }

    /// <summary>
    /// Generates a single-use reset token for the account and emails the reset link. Silently does
    /// nothing when no account exists for the email, so the caller cannot tell the two apart.
    /// </summary>
    /// <remarks>
    /// Deliberately returns nothing. This once returned the token so the controller could echo it
    /// in the Development response body, which handed a live credential to any unauthenticated
    /// caller who knew an email address (GHSA-qmqp-gxpr-867g). The token now leaves this method
    /// only inside the emailed link; a Development stack reads it back through the file drop
    /// configured by DEV_MAIL_DROP_PATH.
    /// </remarks>
    public async Task RequestPasswordResetAsync(string email, CancellationToken ct = default)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
            return;

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var url = $"{BaseUrl}/reset-password?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
        await emailSender.SendAsync(AccountEmailTemplates.PasswordReset(email, url), ct);
    }

    /// <summary>
    /// Begins a verify-then-apply email change: emails a confirmation link to the new address. The account
    /// email is unchanged until <see cref="ConfirmEmailChangeAsync"/> runs. Enumeration-safe — if the target
    /// address already belongs to another account, nothing is sent.
    /// </summary>
    public async Task RequestEmailChangeAsync(Guid userId, string newEmail, string? currentPassword, CancellationToken ct = default)
    {
        newEmail = newEmail?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(newEmail))
            throw new InvalidOperationException("Email address cannot be empty.");

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return;

        await RequireCurrentPasswordAsync(user, currentPassword);

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
            ?? throw new InvalidOperationException(InvalidResetRequest);

        var result = await userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
        {
            // An unknown address and a bad token must read identically, or the wording is an
            // enumeration oracle: this used to answer "Invalid or expired password reset request."
            // for an address with no account and Identity's "Invalid token." for one that had
            // (GHSA-28pc-cx5w-g6jp). Identity verifies the token before it validates the new
            // password and returns on the first failure, so an InvalidToken result is exactly that
            // case. Password-policy errors are only reachable once a valid token has been
            // presented — by someone who already controls the mailbox — so those still surface.
            if (result.Errors.Any(e => e.Code == "InvalidToken"))
                throw new InvalidOperationException(InvalidResetRequest);

            throw new InvalidOperationException(
                string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        // Following the emailed link proves control of the mailbox just as the confirmation link does,
        // so a reset also confirms the address. Without this, anyone whose confirmation email went
        // astray would have no self-service route back in — a reset would succeed and sign-in would
        // still be refused.
        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            await userManager.UpdateAsync(user);
        }

        await refreshTokens.RevokeAllActiveAsync(user.Id, ct);
        // Devices are forgotten alongside the sessions. Changing or resetting a password is
        // the documented remedy for a machine the Driver no longer trusts, and a device record
        // outliving it would leave its holder a standing guessing allowance against the new
        // password — see KnownDeviceStore.ForgetAllAsync.
        await knownDevices.ForgetAllAsync(user.Id, ct);
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
        // Devices are forgotten alongside the sessions. Changing or resetting a password is
        // the documented remedy for a machine the Driver no longer trusts, and a device record
        // outliving it would leave its holder a standing guessing allowance against the new
        // password — see KnownDeviceStore.ForgetAllAsync.
        await knownDevices.ForgetAllAsync(user.Id, ct);
    }

    // TODO: Validate state against a nonce store to prevent CSRF; exchange the authorization
    //       code for an iRacing access token via the Authorization Code flow; fetch driver
    //       profile (customerId, displayName) from iRacing; update ApplicationUser.IRacingCustomerId;
    //       re-issue JWT with updated claims
    public Task<AuthResultDto> HandleCallbackAsync(string code, string state, CancellationToken ct = default)
        => throw new NotImplementedException();

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The single wording every password-reset failure that is not about the submitted password uses.
    /// </summary>
    private const string InvalidResetRequest = "Invalid or expired password reset request.";

    /// <summary>
    /// Identity's codes for "this account already exists". They are the only <see cref="RegisterAsync"/>
    /// failures that describe the store rather than the submitted values, so they are the only ones
    /// withheld from the caller.
    /// </summary>
    private static bool IsDuplicateAccount(string code) =>
        code is "DuplicateUserName" or "DuplicateEmail";

    /// <summary>
    /// Tells the owner of an already-registered address that someone tried to register it. An
    /// unconfirmed account gets its confirmation link resent — that caller is almost always the same
    /// person retrying because the first email never arrived, and telling them to reset a password
    /// they already know would be useless. A confirmed account gets a security notice instead.
    /// </summary>
    private async Task NotifyAddressAlreadyRegisteredAsync(string email, CancellationToken ct)
    {
        var existing = await userManager.FindByEmailAsync(email);
        if (existing is null)
            return;

        if (await userManager.IsEmailConfirmedAsync(existing))
            await emailSender.SendAsync(
                AccountEmailTemplates.DuplicateRegistration(email, $"{BaseUrl}/forgot-password"), ct);
        else
            await SendEmailConfirmationAsync(existing, ct);
    }

    /// <summary>
    /// Emails the confirmation link. Like every account link, the token is a single-use credential that
    /// leaves the server only inside the email — never a response body and never a log.
    /// </summary>
    private async Task SendEmailConfirmationAsync(ApplicationUser user, CancellationToken ct)
    {
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var url = $"{BaseUrl}/verify-email?userId={user.Id}&token={Uri.EscapeDataString(token)}";
        await emailSender.SendAsync(AccountEmailTemplates.EmailConfirmation(user.Email!, url), ct);
    }

    private async Task RequireCurrentPasswordAsync(ApplicationUser user, string? currentPassword)
    {
        if (string.IsNullOrEmpty(currentPassword) || !await userManager.CheckPasswordAsync(user, currentPassword))
            throw new InvalidOperationException("Current password is incorrect.");
    }

    internal async Task<string> GenerateJwtAsync(ApplicationUser user)
    {
        var creds = jwt.IssuingCredentials();

        var roles = await userManager.GetRolesAsync(user);
        var role = roles.FirstOrDefault() ?? "Standard";

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
            issuer: jwt.Issuer,
            audience: jwt.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(AccessTokenMinutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
