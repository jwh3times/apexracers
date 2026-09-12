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
    SignInThrottleStore signInThrottle,
    KnownDeviceStore knownDevices,
    IOutboundEmailQueue emailQueue,
    ILogger<AuthService> logger)
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
            Id          = Guid.NewGuid(),
            UserName    = email,
            Email       = email,
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
    /// Signs a caller in, or returns null. **Every** refusal is the same null — unknown address,
    /// unconfirmed address, throttled address, and wrong password are indistinguishable to the caller
    /// (GHSA-28pc-cx5w-g6jp, GHSA-72v6-mw4c-q96r). The return type carries no reason on purpose;
    /// there is nowhere to put one, so a future change cannot reopen the channel by accident.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The throttle is deliberately silent here rather than reported once the right password arrives.
    /// Reporting it on a correct password would turn the throttle window into a password oracle: an
    /// attacker who kept guessing through it would learn they had found the password from that
    /// response alone, which is precisely what the throttle exists to prevent. The account's owner is
    /// told by email instead.
    /// </para>
    /// <para>
    /// <paramref name="sourceAddress"/> is what the throttle is scoped to, and it is only meaningful
    /// because forwarded headers are processed at the edge (GHSA-fq5w-frqr-6px2) — a caller who could
    /// forge it would hand themselves a fresh allowance per request. It is optional so that callers
    /// with no request context (tests, tooling) still compile; those all share one bucket rather than
    /// bypassing the throttle, which is why <see cref="SignInThrottleStore.NormaliseAddress"/> maps
    /// absence to a single stand-in rather than to a unique value.
    /// </para>
    /// <para>
    /// One residual, stated rather than glossed: the throttle read below happens only for an address
    /// that has a confirmed account, so that path now does two indexed lookups the unknown and
    /// unconfirmed paths do not. <see cref="RefuseWithoutDisclosing"/> equalises the dominant cost —
    /// the password hash — and its own remarks already concede it closes the measurable gap rather
    /// than making the whole request constant time; this widens the remainder slightly. Two primary
    /// key/unique-index lookups against the surrounding PBKDF2 work is not a practical oracle, and
    /// the alternative — reading throttle rows for accounts that do not exist — needs a user id there
    /// is no way to have. Worth knowing before anyone adds more work to this branch.
    /// </para>
    /// <para>
    /// The known-device exemption (issue #314) adds two residuals of its own, and deliberately does
    /// not add a third. It does <em>not</em> widen the allowance: a caller holding a cookie is
    /// charged against the device and the address together, so spending the device's window spends
    /// the address's too and dropping the cookie afterwards buys nothing. An earlier draft charged
    /// only the device and recorded the resulting sum as inherent; it is not — this is what closes
    /// it, and it also makes every cookie-bearing attempt perform the same two claims regardless of
    /// whether the cookie is recognised or spent, so there is no longer a shape to time.
    /// </para>
    /// <para>
    /// What remains: a device-scoped failure still feeds the account-wide counter, as it must, so a
    /// copied cookie can help push an account over the high-water mark and tighten every
    /// <em>unrecognised</em> address — including the Driver's other browsers. And a holder who
    /// shares the Driver's address can spend both scopes and deny them until one of the windows
    /// lapses; that needs the cookie <em>and</em> the shared egress, which is strictly more than
    /// #314's original attack required, and one successful sign-in clears the device again.
    /// </para>
    /// </remarks>
    /// <param name="request">The submitted address and password.</param>
    /// <param name="sourceAddress">Post-forwarded-headers client address; see the remarks.</param>
    /// <param name="deviceToken">
    /// The known-device cookie the caller presented, if any (issue #314). Resolved before the
    /// account is looked up, which is what keeps the exemption from timing an account oracle — see
    /// the call site.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<SignInOutcome?> SignInAsync(
        LoginRequest request,
        string? sourceAddress,
        string? deviceToken,
        CancellationToken ct = default)
    {
        var address = SignInThrottleStore.NormaliseAddress(sourceAddress);

        // Resolved BEFORE the account is looked up, and keyed on the presented cookie alone. The
        // ordering is the whole of issue #314's "the exemption must not itself become an oracle":
        // this lookup happens for every caller who sends a cookie and for no caller who does not, so
        // its cost tracks something the caller already knows rather than whether the address they
        // typed has an account. Doing it after the account was found would add work to exactly the
        // branch the remarks above already flag as the thin end of a timing difference.
        var device = await knownDevices.RecogniseAsync(deviceToken, ct);

        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
            return RefuseSignInWithoutDisclosing(request.Password);

        // An account whose address was never confirmed has to be indistinguishable from one that does
        // not exist, or registration is still an enumeration oracle: an attacker who registers a
        // victim's address could read the answer off whether the credentials they just chose work
        // (GHSA-72v6-mw4c-q96r). Checked ahead of both the lockout state and the password so an
        // unconfirmed account can be neither probed nor locked out by a stranger.
        if (!await userManager.IsEmailConfirmedAsync(user))
            return RefuseSignInWithoutDisclosing(request.Password);

        // An address that has used up its allowance is refused before the password is even checked,
        // so a correct guess from a machine that has already failed too often still fails. Nothing is
        // recorded on this path: an attempt that was never checked carries no information, and
        // counting it would let a caller hold their own window open by continuing to knock.
        //
        // The allowance is per (account, source address), NOT per account — that is the whole of
        // issue #300. An account-wide counter meant five requests from a stranger locked the Driver
        // out of their own account for fifteen minutes, repeatable forever. Now a stranger exhausts
        // only their own address; the Driver's machine has a clean record and signs in normally.
        // Claimed, not merely read. The claim and the count the gate tests are one statement, so a
        // burst of concurrent requests gets distinct counts instead of all reading zero and all
        // getting their password checked — see ClaimAttemptAsync for why that gap is wide enough to
        // drive through.
        // A device this account has already signed in from is throttled against its own counter
        // instead of the address's (issue #314). That is what makes the exemption worth anything:
        // raising the address allowance would not help, because on a shared egress the attacker is
        // exhausting the very row the Driver would be measured against. The ownership test is here
        // rather than in the lookup so recognition stays account-independent.
        var onKnownDevice = device is not null && device.UserId == user.Id;
        var deviceSpent = false;

        bool refused;
        if (onKnownDevice)
        {
            // A caller holding this account's cookie is charged against **both** scopes and judged
            // on the device's verdict alone. Two properties fall out of that, and both matter:
            //
            // 1. The allowance stops being additive. Charging only the device would let a guesser
            //    spend its window, drop the cookie, and spend the address's as well — which they
            //    could do by hand whether or not this code fell through, so the ceiling was really
            //    the sum of the two. Spending both together makes it the larger of the two instead.
            // 2. The work no longer varies with whether the cookie is recognised or spent. Every
            //    cookie-bearing attempt performs exactly the same two claims, so the only thing a
            //    holder can time is something they already know.
            //
            // The address's verdict is deliberately discarded while the device has room: that is
            // the whole of #314, since on a shared egress the address is exactly what the attacker
            // has exhausted.
            var deviceClaim = await knownDevices.ClaimAttemptAsync(device!.Id, user.Id, ct);
            var addressClaim = await signInThrottle.ClaimAttemptAsync(user.Id, address, ct);

            if (deviceClaim is { Refused: false } permitted)
            {
                refused = false;
                deviceSpent = permitted.Spent;
            }
            else
            {
                // **A device may only ever add allowance, never remove one.** Reached when the row
                // vanished between recognition and the claim, and — the case that matters — when its
                // own window is spent. Refusing here would have handed anyone who copied a cookie a
                // way to deny the Driver with the correct password from any network, because the
                // Driver's browser presents that same cookie: issue #300's shape rebuilt on a new
                // key, and cheaper to reach than the shared egress #314 set out to fix. Falling
                // through leaves the Driver exactly the route they would have had with no exemption
                // at all. The one case it cannot rescue is a holder who *also* shares the Driver's
                // address, since both scopes are then spent — strictly harder than #314's original
                // shared-egress attack, and self-healing after one successful sign-in clears the
                // device below.
                onKnownDevice = false;
                refused = addressClaim.Refused;
            }
        }
        else
        {
            refused = (await signInThrottle.ClaimAttemptAsync(user.Id, address, ct)).Refused;
        }

        if (refused)
            return RefuseSignInWithoutDisclosing(request.Password);

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            // Returns true only for the one caller entitled to send the owner's notice; the store
            // paces that to one email per account per interval, because sign-in is unauthenticated
            // and an email per lockout would be an inbox a stranger could fill.
            //
            // Queued rather than sent. Sending it here would put an outbound mail round trip inside
            // the response for accounts that exist and nowhere else, which is an account oracle a
            // single request can read off the clock — and a mail failure would surface as a 500 on
            // exactly those accounts, which is the same oracle by status code that this endpoint's
            // 423 once was (GHSA-28pc-cx5w-g6jp).
            // The owner is told when the scope that was actually used runs out, whichever that was.
            // An earlier draft skipped that on the device path, reasoning that a recognised browser
            // running out is the owner mistyping. That is true of the common case and irrelevant to
            // the adversarial one: with the shipped numbers a device-scoped caller tops out well
            // below the account-wide high-water mark, so the notice would never have fired at all
            // and someone holding a copied cookie could guess indefinitely unobserved — removing
            // the only detection signal for precisely the adversary this exemption empowers.
            // Pacing is already the store's job, so there is no inbox to flood by telling the truth
            // here.
            if (await signInThrottle.NoteFailureAsync(user.Id, onKnownDevice ? null : address, deviceSpent, ct))
                QueueSuspiciousAttemptsNotice(user);

            return null;
        }

        // This account's own device is cleared on **any** success, not only when it was the scope
        // that let the caller in. It is the fall-through case that makes that necessary: a thief who
        // pinned the device counter leaves it spent, and if the Driver's successful sign-in through
        // the address did not reset it, the exemption would stay switched off for the rest of that
        // window — exactly while the attack is running. One correct password now heals it.
        if (device is { } owned && owned.UserId == user.Id)
            await knownDevices.ClearFailuresAsync(owned.Id, ct);

        // The address is cleared only when it was the gating scope, preserving the pre-existing
        // rule. Clearing it on a device-scoped success would reset a counter the Driver may be
        // sharing with whoever is guessing — handing them a fresh allowance on the strength of the
        // victim's own sign-in. The account-wide counter is left to expire on its own for the same
        // reason: the owner signing in is not evidence that the guessing has stopped.
        if (!onKnownDevice)
            await signInThrottle.ClearAddressAsync(user.Id, address, ct);

        // Remembering the browser is bookkeeping, not authentication, and it must not be able to
        // cost a caller who supplied the right password their sign-in. A device-table fault would
        // otherwise surface as a 500 on correct credentials; the worst case here is a lost
        // exemption, which is the safe direction. Cancellation is left to propagate — that is the
        // caller giving up, not a fault.
        IssuedDevice? issued = null;
        try
        {
            issued = await knownDevices.RememberAsync(user.Id, device, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not remember the signing-in device; continuing without the exemption.");
        }

        var jwt     = await GenerateJwtAsync(user);
        var refresh = await refreshTokens.IssueAsync(user.Id, ct);
        return new SignInOutcome(
            new AuthResultDto(jwt, user.Id, user.DisplayName, refresh),
            issued);
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

    /// <summary>
    /// Refuses a sign-in without disclosing why, paying the password-hash cost first. The
    /// <see cref="SignInOutcome"/>-shaped counterpart to <see cref="RefuseWithoutDisclosing"/>.
    /// </summary>
    private SignInOutcome? RefuseSignInWithoutDisclosing(string? password)
    {
        RefuseWithoutDisclosing(password);
        return null;
    }

    public async Task<AuthResultDto> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName))
            throw new InvalidOperationException("Display name cannot be empty.");

        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException("User not found.");

        if (request.IRacingCustomerId.HasValue && request.IRacingCustomerId != user.IRacingCustomerId)
            await RequireCurrentPasswordAsync(user, request.CurrentPassword);

        user.DisplayName = request.DisplayName.Trim();
        if (request.IRacingCustomerId.HasValue)
            user.IRacingCustomerId = request.IRacingCustomerId.Value;
        if (!string.IsNullOrWhiteSpace(request.ThemePreference) &&
            request.ThemePreference is "auto" or "light" or "dark")
            user.ThemePreference = request.ThemePreference;

        IdentityResult result;
        try
        {
            result = await userManager.UpdateAsync(user);
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
    /// A real hash of a value no account uses, held for the life of the process. Cached because
    /// producing it costs the same as verifying it, and paying that twice on a refused request would
    /// overshoot the very cost this is meant to match.
    /// </summary>
    private static string? _dummyPasswordHash;

    /// <summary>
    /// Stand-in passed to the hasher on the refusal path. <see cref="PasswordHasher{TUser}"/> ignores
    /// the user, but the parameter is not optional and <see cref="ApplicationUser"/> has required
    /// members, so one throwaway instance serves every call.
    /// </summary>
    private static readonly ApplicationUser HashStandIn = new() { DisplayName = string.Empty };

    /// <summary>
    /// Refuses a sign-in after doing the password work the accepted path would have done.
    /// </summary>
    /// <remarks>
    /// Without this, an address with no account answers before any hash is computed while a real one
    /// pays the full PBKDF2 cost, and the gap is wide enough to read account existence off the clock
    /// (GHSA-28pc-cx5w-g6jp). Verifying against a stand-in hash puts the same dominant cost on both
    /// paths. It equalises that cost, not the whole request — the surrounding database work still
    /// differs slightly — so treat it as closing the measurable gap rather than as constant time.
    /// </remarks>
    private AuthResultDto? RefuseWithoutDisclosing(string? password)
    {
        // Built from the injected hasher rather than a fresh one, so it keeps matching the configured
        // work factor if that is ever tuned. A race here is harmless: both racers compute an equally
        // valid hash and the cost is identical either way.
        _dummyPasswordHash ??= userManager.PasswordHasher.HashPassword(HashStandIn, "not-a-real-password");
        userManager.PasswordHasher.VerifyHashedPassword(
            HashStandIn, _dummyPasswordHash, password ?? string.Empty);
        return null;
    }

    /// <summary>
    /// Tells the account's owner that something is guessing at it, which is the only disclosure that
    /// reaches them rather than whoever is guessing. See <see cref="LoginAsync"/> for why the HTTP
    /// response cannot carry it, and the template for why it does not say "locked".
    /// </summary>
    /// <remarks>
    /// Queued rather than sent, and so returns nothing to await. This is the one account email whose
    /// send must not happen inside the request: it fires only for addresses that have an account, so
    /// an inline send would price sign-in differently for real and unknown addresses and hand back
    /// the account oracle by latency, or by a 500 when the mail provider is unhappy. See
    /// <see cref="IOutboundEmailQueue"/>.
    /// </summary>
    private void QueueSuspiciousAttemptsNotice(ApplicationUser user)
    {
        if (string.IsNullOrEmpty(user.Email))
            return;

        emailQueue.Enqueue(
            AccountEmailTemplates.SuspiciousSignInAttempts(user.Email, $"{BaseUrl}/forgot-password"));
    }

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
        var url   = $"{BaseUrl}/verify-email?userId={user.Id}&token={Uri.EscapeDataString(token)}";
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

