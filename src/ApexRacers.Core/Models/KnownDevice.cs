namespace ApexRacers.Core.Models;

/// <summary>
/// A browser that has completed a successful sign-in to one account, and the failures it has
/// accumulated since. The second dimension of identity that
/// <see cref="ApexRacers.Core.SignInThrottle"/>'s remarks say the shared-egress residual needs
/// (issue #314).
/// </summary>
/// <remarks>
/// <para>
/// Sign-in throttling is scoped to (account, source address), which closed issue #300's denial of
/// service but cannot help when the attacker <em>shares</em> an address with the Driver — a
/// carrier-grade NAT pool, an employer's egress, a shared VPN exit. Every rule keyed on the address
/// keys on something both parties hold. A device the Driver has already signed in from is the first
/// thing in this design that the attacker on the same network does not have, so it is what the
/// exemption is built on: a recognised device is throttled against <em>its own</em> counter here and
/// the address counter is not consulted for it at all.
/// </para>
/// <para>
/// <b>Why not the two mechanisms issue #314 proposed.</b> A recorded successful
/// <c>(user, address)</c> pair fails for the reason above — on a shared egress the attacker's
/// address is the Driver's own known-good address, so both look "known" and it separates nobody. A
/// valid refresh token does separate them, but refresh tokens live seven days and anyone holding an
/// unexpired one is kept signed in by the silent-refresh flow, so it is absent in exactly the case
/// that matters: a Driver who is at the sign-in page because their session has already lapsed. This
/// record outlives the session deliberately, which is the whole reason it is a separate thing from
/// <see cref="RefreshToken"/>.
/// </para>
/// <para>
/// <b>Growth is bounded by construction.</b> Rows are written only after a password has actually
/// been verified, so an unauthenticated caller cannot create one — the property the throttle tables
/// buy with counters instead of logs, obtained here for free. A per-account cap then bounds an
/// authenticated one, and <see cref="ExpiresAt"/> bounds the rest.
/// </para>
/// <para>
/// <b>The cookie is not a credential.</b> Presenting this proves nothing on its own and grants no
/// access: it selects which counter a sign-in attempt is throttled against, and the password is
/// still checked exactly as before. Someone who steals one buys a larger guessing allowance against
/// an account they must still guess the password for — not entry. That is why it can be long-lived
/// where a refresh token cannot.
/// </para>
/// <para>
/// <b>It may only ever add allowance, never remove one.</b> A device whose own window is spent falls
/// back to the address scope rather than being refused, and that is not a convenience — it is what
/// keeps the exemption from becoming a new denial of service. The Driver's browser presents the same
/// cookie as anyone who copied it, so refusing an exhausted device would let a thief deny the Driver
/// with the correct password from any network: issue #300's shape rebuilt on a new key, and cheaper
/// to reach than the shared egress this exists to fix.
/// </para>
/// <para>
/// <b>And it does not widen the total allowance.</b> A caller presenting a cookie is charged against
/// this device <em>and</em> their address, and judged on this device alone, so the attempts are the
/// larger of the two windows rather than their sum — dropping the cookie half way through buys
/// nothing, because the address has been paying all along. Discarding the address's verdict while
/// the device has room is the entire point: on a shared egress the address is precisely what the
/// attacker has exhausted.
/// </para>
/// <para>
/// <b>Recognition ends when the account is recovered.</b> Changing a password, resetting one, and
/// changing an email address each forget every device, beside revoking the refresh tokens they
/// already revoked — see <see cref="ApexRacers.Api.Services.KnownDeviceStore.ForgetAllAsync"/>.
/// Without that, the documented remedy for a machine the Driver no longer trusts would leave its
/// holder a standing guessing allowance against the new password for the rest of the 90 days.
/// </para>
/// </remarks>
public class KnownDevice
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>
    /// SHA-256 of the cookie value, lowercase hex — the same shape as
    /// <see cref="RefreshToken.TokenHash"/>. The raw value exists only in the cookie, so a reader of
    /// this table cannot present one.
    /// </summary>
    public string TokenHash { get; set; } = "";

    /// <summary>When the device first signed in successfully.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Most recent successful sign-in from this device. Drives both the per-account cap's eviction
    /// order and the purge, so an abandoned browser ages out rather than holding a slot forever.
    /// </summary>
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>When the record stops being recognised, whatever the cookie still says.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Failures recorded in the window that opened at <see cref="WindowStartedAt"/>.</summary>
    public int FailureCount { get; set; }

    /// <summary>
    /// When the current failure window opened. Never moved by a later failure, for the same reason
    /// <see cref="SignInAddressFailure.WindowStartedAt"/> is not: a window that restarted on every
    /// failure could be held open indefinitely by a caller who kept failing.
    /// </summary>
    public DateTimeOffset WindowStartedAt { get; set; }
}
