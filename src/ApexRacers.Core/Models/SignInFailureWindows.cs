namespace ApexRacers.Core.Models;

/// <summary>
/// Failures one source address has accumulated against one account. The row an attacker can create
/// and exhaust without touching anyone else's ability to sign in.
/// </summary>
/// <remarks>
/// <para>
/// One row per (account, address) rather than one row per attempt. An attempt log would be simpler
/// and race-free, but its growth is driven by an unauthenticated caller — a thousand addresses
/// guessing for an hour would write hundreds of thousands of rows. Counters bound that to one row
/// per address, which <see cref="ApexRacers.Core.SignInThrottle"/>'s tightened allowance then makes
/// expensive to multiply.
/// </para>
/// <para>
/// <see cref="IpAddress"/> is the address as the request presented it after forwarded-header
/// processing, which is trustworthy here only because the deployment terminates at a proxy that
/// rewrites it (GHSA-fq5w-frqr-6px2). Without that, an attacker could forge a different address per
/// request and hand themselves an unlimited allowance — the whole scheme rests on it.
/// </para>
/// </remarks>
public class SignInAddressFailure
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>
    /// Source address, normalised to its string form. Sized for an IPv6 address with a scope id.
    /// </summary>
    public string IpAddress { get; set; } = "";

    /// <summary>Failures recorded in the window that opened at <see cref="WindowStartedAt"/>.</summary>
    public int FailureCount { get; set; }

    /// <summary>
    /// When the current window opened. Never moved by a later failure — see
    /// <see cref="ApexRacers.Core.SignInThrottle.RecordFailure"/> for why a sliding window could be
    /// held open forever.
    /// </summary>
    public DateTimeOffset WindowStartedAt { get; set; }

    /// <summary>Most recent failure, used only to decide when the row is safe to purge.</summary>
    public DateTimeOffset LastFailureAt { get; set; }
}

/// <summary>
/// Failures against one account across every source address. Read only to decide whether the
/// account is under distributed attack; it never denies a sign-in on its own.
/// </summary>
/// <remarks>
/// Kept as its own row rather than reusing ASP.NET Identity's <c>AccessFailedCount</c> and
/// <c>LockoutEnd</c>. Those columns still exist on the user and are deliberately no longer consulted:
/// repurposing <c>IsLockedOutAsync</c> to mean "under attack" rather than "denied" would read as
/// denial to everyone who met it later, which is precisely the behaviour issue #300 removed.
/// </remarks>
public class SignInAccountFailure
{
    /// <summary>The account. Also the primary key — one row per account, so this cannot grow.</summary>
    public Guid UserId { get; set; }

    /// <summary>Failures recorded in the window that opened at <see cref="WindowStartedAt"/>.</summary>
    public int FailureCount { get; set; }

    /// <summary>When the current window opened. Fixed for the life of the window.</summary>
    public DateTimeOffset WindowStartedAt { get; set; }

    /// <summary>Most recent failure, used only to decide when the row is safe to purge.</summary>
    public DateTimeOffset LastFailureAt { get; set; }

    /// <summary>
    /// When the owner was last told that something is guessing at their account, or null if never.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a rate limit on a security notice, and it has to exist. Sign-in is unauthenticated,
    /// so anything that emails the owner per lockout hands a stranger a way to fill their inbox —
    /// one message per source address, from as many addresses as they care to use. That would be a
    /// worse denial of service than the one this whole change removes.
    /// </para>
    /// <para>
    /// It also lives on the account rather than the address row on purpose. Per-address throttling
    /// would be no throttle at all: a new address is free.
    /// </para>
    /// </remarks>
    public DateTimeOffset? NoticeSentAt { get; set; }
}
