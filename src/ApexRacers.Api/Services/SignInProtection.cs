using ApexRacers.Api.Dtos;
using ApexRacers.Api.Services.Email;
using ApexRacers.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

/// <summary>Internal authentication evidence, never serialized as an HTTP response.</summary>
public sealed record ProtectedSignIn(ApplicationUser User, IssuedDevice? Device);

/// <summary>
/// Owns the sign-in protection lifecycle. Callers receive an authenticated User or a generic refusal;
/// device ownership, admission scopes, password work, notices and cleanup stay inside this module.
/// </summary>
public sealed class SignInProtection(
    UserManager<ApplicationUser> userManager,
    AppDbContext db,
    IConfiguration config,
    SignInThrottleStore signInThrottle,
    KnownDeviceStore knownDevices,
    IOutboundEmailQueue emailQueue,
    ILogger<SignInProtection> logger)
{
    private string BaseUrl => config["APP_BASE_URL"]?.TrimEnd('/') ?? "https://apexracers.gg";

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
    /// Confirmed accounts perform coordination and throttle database work that unknown and
    /// unconfirmed accounts do not. <see cref="RefuseWithoutDisclosing"/> equalises the dominant
    /// password-hash cost; it does not make the whole request constant time or establish that the
    /// remaining timing difference is unobservable. Keep that distinction when extending this path.
    /// </para>
    /// <para>
    /// A caller holding this account's recognised cookie is charged against both the device and
    /// the address. In sequential use, spending the device's window also spends the address's,
    /// so dropping the cookie afterwards buys no extra allowance. Both scopes are claimed inside
    /// one short per-User coordination transaction, including the address-only admission path.
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
    public async Task<ProtectedSignIn?> AuthenticateAsync(
        LoginRequest request,
        string? sourceAddress,
        string? deviceToken,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
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
            return RefuseWithoutDisclosing(request.Password);

        // An account whose address was never confirmed has to be indistinguishable from one that does
        // not exist, or registration is still an enumeration oracle: an attacker who registers a
        // victim's address could read the answer off whether the credentials they just chose work
        // (GHSA-72v6-mw4c-q96r). Checked ahead of both the lockout state and the password so an
        // unconfirmed account can be neither probed nor locked out by a stranger.
        if (!await userManager.IsEmailConfirmedAsync(user))
            return RefuseWithoutDisclosing(request.Password);

        var admission = await ClaimAdmissionAsync(user, device, address, ct);

        if (admission.Refused)
            return RefuseWithoutDisclosing(request.Password);

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
            if (await signInThrottle.NoteFailureAsync(user.Id, admission.OnKnownDevice ? null : address, admission.DeviceSpent, ct))
                QueueSuspiciousAttemptsNotice(user);

            return null;
        }

        // This account's own device is cleared on **any** success, not only when it was the scope
        // that let the caller in. It is the fall-through case that makes that necessary: a thief who
        // pinned the device counter leaves it spent, and if the Driver's successful sign-in through
        // the address did not reset it, the exemption would stay switched off for the rest of that
        // window — exactly while the attack is running. One correct password now heals it.
        if (device is { } owned && owned.UserId == user.Id)
        {
            try
            {
                await knownDevices.ClearFailuresAsync(owned.Id, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogDeviceFailure(ex);
            }
        }

        // The address is cleared only when it was the gating scope, preserving the pre-existing
        // rule. Clearing it on a device-scoped success would reset a counter the Driver may be
        // sharing with whoever is guessing — handing them a fresh allowance on the strength of the
        // victim's own sign-in. The account-wide counter is left to expire on its own for the same
        // reason: the owner signing in is not evidence that the guessing has stopped.
        if (!admission.OnKnownDevice)
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
            LogDeviceFailure(ex);
        }

        return new ProtectedSignIn(user, issued);
    }

    private sealed record Admission(bool Refused, bool OnKnownDevice, bool DeviceSpent);

    private async Task<Admission> ClaimAdmissionAsync(
        ApplicationUser user, KnownDeviceRecord? device, string address, CancellationToken ct)
    {
        // One per-User ordering covers owned-device and address-only callers alike. Hold it only
        // across database admission: password work, notices and successful cleanup run after disposal.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var owner = await db.Database.SqlQueryRaw<Guid>(
            """
            SELECT "Id" AS "Value" FROM identity."Users" WHERE "Id" = {0} FOR UPDATE
            """, user.Id).ToListAsync(ct);
        if (owner.Count == 0)
        {
            await transaction.CommitAsync(ct);
            return new Admission(true, false, false);
        }

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
            // 1. In sequential use the allowance stops being additive. Charging only the device
            //    would let a guesser
            //    spend its window, drop the cookie, and spend the address's as well — which they
            //    could do by hand whether or not this code fell through, so the ceiling was really
            //    the sum of the two. The per-User coordination keeps both claims in the same order
            //    for concurrent callers, including callers who omit the cookie.
            // 2. An owned recognised cookie performs both claims whether its device is permitted
            //    or spent; a spent device still retains the ordinary address route.
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

        await transaction.CommitAsync(ct);
        return new Admission(refused, onKnownDevice, deviceSpent);
    }

    private void LogDeviceFailure(Exception ex) =>
        logger.LogWarning("Could not update the signing-in device; continuing without device bookkeeping. Failure type: {FailureType}", ex.GetType().Name);

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
    private ProtectedSignIn? RefuseWithoutDisclosing(string? password)
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
    /// reaches them rather than whoever is guessing. See <see cref="AuthenticateAsync"/> for why the HTTP
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

        try
        {
            emailQueue.Enqueue(
                AccountEmailTemplates.SuspiciousSignInAttempts(user.Email, $"{BaseUrl}/forgot-password"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Could not queue the sign-in security notice. Failure type: {FailureType}", ex.GetType().Name);
        }
    }

}
