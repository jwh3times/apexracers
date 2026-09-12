using System.Security.Cryptography;
using System.Text;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

/// <summary>
/// Recognises the browser a sign-in came from, and throttles it against its own counter. The
/// persistence half of issue #314's known-device exemption; see <see cref="KnownDevice"/> for why
/// the exemption keys on a device rather than on an address or a refresh token.
/// </summary>
/// <remarks>
/// <para>
/// <b>Recognition is deliberately keyed on the presented token alone</b>, never on the account the
/// caller named. That is what keeps the exemption from becoming the enumeration oracle issue #314
/// warns about: the work this does is decided by whether the <em>caller</em> sent a cookie, which
/// the caller already knows, and not by whether the address they typed has an account. A lookup
/// performed only for real accounts would time the difference for anyone who cared to measure it —
/// the same shape as the <c>423</c> that GHSA-28pc-cx5w-g6jp removed, arriving by the clock instead
/// of the status code. <see cref="AuthService.LoginAsync"/> calls this before it looks the account
/// up, and the ownership check happens afterwards in memory.
/// </para>
/// <para>
/// Every counter write is one statement, for the reason
/// <see cref="SignInThrottleStore.ClaimAttemptAsync"/> gives at length: a read, a password hash, and
/// a write is three steps with two gaps, and a concurrent burst drives straight through them.
/// </para>
/// </remarks>
public sealed class KnownDeviceStore(
    AppDbContext db,
    TimeProvider timeProvider,
    SignInThrottleOptions options)
{
    /// <summary>
    /// How long a device stays recognised after its last successful sign-in. Long on purpose: the
    /// case this exists for is a Driver whose session has lapsed, so a lifetime near the refresh
    /// token's seven days would be absent exactly when it is needed.
    /// </summary>
    public static readonly TimeSpan DeviceLifetime = TimeSpan.FromDays(90);

    /// <summary>
    /// Devices remembered per account, oldest-seen evicted first. Bounds a table that only an
    /// authenticated caller can grow; the unauthenticated bound is that a row needs a correct
    /// password to exist at all.
    /// </summary>
    public const int MaxDevicesPerUser = 10;

    /// <summary>
    /// Resolves a presented cookie value to the device it names, or null for absent, malformed,
    /// unknown, or expired. Does not consider which account the caller claimed to be.
    /// </summary>
    public async Task<KnownDeviceRecord?> RecogniseAsync(string? presentedToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(presentedToken) || presentedToken.Length > MaxTokenLength)
            return null;

        var hash = HashToken(presentedToken);
        var now = timeProvider.GetUtcNow();

        var row = await db.KnownDevices
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.TokenHash == hash, ct);

        if (row is null || row.ExpiresAt <= now)
            return null;

        return new KnownDeviceRecord(row.Id, row.UserId) { Token = presentedToken };
    }

    /// <summary>
    /// Claims one attempt against a recognised device, or returns <c>null</c> if the device is no
    /// longer there to claim against.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An <c>UPDATE … RETURNING</c> rather than the upsert the address path needs, because the row
    /// is known to exist — recognition just read it. The window is preserved while it is still
    /// current and reopened only once it has run out, so it cannot be held open by a caller who
    /// keeps failing.
    /// </para>
    /// <para>
    /// <c>null</c> rather than a sentinel decision. A concurrent purge or account deletion can
    /// remove the row between recognition and this claim, and a "refused" or zero-allowance
    /// stand-in would read as <em>exhausted</em> to any caller that checked only the verdict —
    /// denying a Driver on the strength of a device that no longer exists. Absence is its own
    /// answer, and the caller falls back to the address scope.
    /// </para>
    /// <para>
    /// The statement matches on the owner as well as the row. The in-memory ownership check at the
    /// call site is the one that decides the exemption; this makes the same invariant hold at the
    /// storage layer, so no future caller can increment another account's device by passing the
    /// wrong pair.
    /// </para>
    /// </remarks>
    public async Task<KnownDeviceClaim?> ClaimAttemptAsync(
        Guid deviceId,
        Guid userId,
        CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();
        var windowFloor = now - options.PerAddressWindow;

        var claimedRows = await db.Database
            .SqlQueryRaw<int>(
                """
                UPDATE identity."KnownDevices" SET
                    "FailureCount" = CASE
                        WHEN "WindowStartedAt" > {2}
                        THEN "FailureCount" + 1
                        ELSE 1
                    END,
                    "WindowStartedAt" = CASE
                        WHEN "WindowStartedAt" > {2}
                        THEN "WindowStartedAt"
                        ELSE {3}
                    END
                WHERE "Id" = {0} AND "UserId" = {1}
                RETURNING "FailureCount" AS "Value"
                """,
                deviceId, userId, windowFloor, now)
            // Not composable — see SignInThrottleStore.ClaimAttemptAsync for why Single() would be
            // rejected by the provider here.
            .ToListAsync(ct);

        if (claimedRows.Count == 0)
            return null;

        var claimed = claimedRows.Single();

        var accountRow = await db.SignInAccountFailures
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.UserId == userId, ct);

        var underAttack = SignInThrottle.IsUnderAttack(
            accountRow is null ? null : new FailureWindow(accountRow.FailureCount, accountRow.WindowStartedAt),
            now,
            options);

        // Known devices keep the ordinary allowance while the account is under attack — the whole
        // point of the exemption. Taken from the shared rule rather than restated, so the device
        // path cannot keep an old allowance after the address path's is changed.
        var allowance = SignInThrottle.AllowanceFor(underAttack, knownDevice: true, options);

        return new KnownDeviceClaim(
            Refused: claimed > allowance,
            Spent: claimed >= allowance,
            UnderAttack: underAttack);
    }

    /// <summary>Clears a device's failures after it proved it holds the password.</summary>
    public async Task ClearFailuresAsync(Guid deviceId, CancellationToken ct = default) =>
        await db.KnownDevices
            .Where(d => d.Id == deviceId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(d => d.FailureCount, 0)
                      .SetProperty(d => d.WindowStartedAt, default(DateTimeOffset)),
                ct);

    /// <summary>
    /// Records that this browser signed in successfully, and returns the cookie value it should
    /// carry from now on. Call only after the password has been verified.
    /// </summary>
    /// <remarks>
    /// A recognised device is renewed in place rather than reissued, so a Driver who signs in
    /// regularly keeps one row instead of minting one per sign-in. Anything else — no cookie, a
    /// cookie nobody recognises, or one belonging to a different account — mints a new record and a
    /// new value.
    /// </remarks>
    public async Task<IssuedDevice> RememberAsync(
        Guid userId,
        KnownDeviceRecord? recognised,
        CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();
        var expiresAt = now + DeviceLifetime;

        if (recognised is { } device && device.UserId == userId)
        {
            var renewed = await db.KnownDevices
                .Where(d => d.Id == device.Id && d.UserId == userId)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(d => d.LastSeenAt, now)
                          .SetProperty(d => d.ExpiresAt, expiresAt),
                    ct);

            // Renewing an existing row keeps its cookie value, which the caller still holds, so
            // nothing needs to be returned to the browser but the same string it sent.
            if (renewed > 0)
                return new IssuedDevice(device.Token, expiresAt);
        }

        var rawToken = MintToken();
        db.KnownDevices.Add(new KnownDevice
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = HashToken(rawToken),
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = expiresAt,
            FailureCount = 0,
            WindowStartedAt = default,
        });
        await db.SaveChangesAsync(ct);

        await EvictExcessAsync(userId, ct);

        return new IssuedDevice(rawToken, expiresAt);
    }

    /// <summary>
    /// Forgets every device remembered for an account. Call wherever a compromise is being remedied.
    /// </summary>
    /// <remarks>
    /// This belongs beside <see cref="RefreshTokenStore.RevokeAllActiveAsync"/>, not instead of it.
    /// Changing or resetting a password is the documented answer to "a machine I used is
    /// compromised", and a device record that outlived it would leave the holder a standing
    /// guessing allowance against the <em>new</em> password for the rest of the 90 days — and,
    /// because the Driver's own browser presents that same cookie, a way to spend the Driver's
    /// device allowance too. Revoking the sessions without forgetting the devices would make the
    /// remedy look complete while leaving the part that matters.
    /// </para>
    /// <para>
    /// The cost, stated rather than glossed: a Driver who resets their password while sharing an
    /// egress with a guesser loses the exemption at the moment it would have helped most, because
    /// the shared address is still spent. That is bounded by the address window rather than by the
    /// device's ninety days, and it is plainly the better side of the trade — but it does mean the
    /// reset is not a way out of a shared-egress attack in progress. Re-remembering the caller's own
    /// browser after a password change would soften it; that is deliberately not done here, because
    /// this method is also reached from the unauthenticated reset completion, where there is no
    /// browser that has proved anything.
    /// </para>
    /// </remarks>
    public async Task<int> ForgetAllAsync(Guid userId, CancellationToken ct = default) =>
        await db.KnownDevices
            .Where(d => d.UserId == userId)
            .ExecuteDeleteAsync(ct);

    /// <summary>Drops this account's least-recently-seen devices once it holds more than the cap.</summary>
    private async Task EvictExcessAsync(Guid userId, CancellationToken ct)
    {
        // Id breaks the tie: ordering by LastSeenAt alone is not deterministic when two devices
        // share an instant, and two sign-ins landing together at the cap could then each evict the
        // row the other had just inserted.
        var keep = await db.KnownDevices
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.LastSeenAt)
            .ThenByDescending(d => d.Id)
            .Select(d => d.Id)
            .Take(MaxDevicesPerUser)
            .ToListAsync(ct);

        if (keep.Count < MaxDevicesPerUser)
            return;

        await db.KnownDevices
            .Where(d => d.UserId == userId && !keep.Contains(d.Id))
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// Longest cookie value considered. A token this mints is 86 characters; anything longer did not
    /// come from here, so it is rejected before it reaches a query rather than hashed and missed.
    /// </summary>
    private const int MaxTokenLength = 256;

    private static string MintToken()
    {
        var bytes = new byte[64];
        RandomNumberGenerator.Fill(bytes);
        // URL-safe and free of the separators a cookie value may not carry, so it needs no further
        // encoding on the way out.
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private static string HashToken(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();
}

/// <summary>
/// A recognised device: which record it is and whose it is. Carries the value the caller presented
/// so a renewal can hand the same one back without re-reading it.
/// </summary>
public sealed record KnownDeviceRecord(Guid Id, Guid UserId)
{
    /// <summary>The cookie value the caller presented, set by the recogniser.</summary>
    public string Token { get; init; } = "";
}

/// <summary>
/// The cookie value a browser should carry, and when it stops being recognised. Both come from the
/// store's own clock so the cookie and the row it names cannot disagree about the expiry.
/// </summary>
public sealed record IssuedDevice(string Token, DateTimeOffset ExpiresAt);

/// <summary>The verdict on one attempt claimed against a device.</summary>
/// <param name="Refused">
/// Whether this attempt is past the device's allowance. The caller must treat this as "fall back to
/// the address scope", never as "deny" — a device may only ever add allowance.
/// </param>
/// <param name="Spent">
/// Whether the device's allowance is now used up, including by this very attempt. Distinct from
/// <paramref name="Refused"/>: the attempt that lands exactly on the allowance is permitted and
/// spends it, and that is the moment the account's owner should hear about it.
/// </param>
/// <param name="UnderAttack">
/// Whether the account-wide high-water mark is crossed. Reported for the owner's notice, never to
/// the caller.
/// </param>
public readonly record struct KnownDeviceClaim(bool Refused, bool Spent, bool UnderAttack);
