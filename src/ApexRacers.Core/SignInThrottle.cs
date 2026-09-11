namespace ApexRacers.Core;

/// <summary>
/// The decision half of sign-in throttling: given what a Driver's account has already seen, decides
/// whether one more attempt from one source address is allowed. Pure — it reads counters and a
/// clock and returns a verdict, so every rule below is testable without a database.
/// </summary>
/// <remarks>
/// <para>
/// Counting failures <em>per account</em> is the obvious design and it is the one that had to go
/// (issue #300). Five failures locked an account for fifteen minutes no matter who produced them,
/// so anyone who knew a Driver's address could keep that Driver signed out indefinitely for the
/// price of five requests a quarter hour. The lockout that existed to stop guessing was a better
/// denial-of-service tool than it was a brute-force control.
/// </para>
/// <para>
/// So failures are counted per <em>(account, source address)</em>. An attacker guessing from their
/// own address exhausts their own allowance and nobody else's; the Driver's address has a clean
/// record and signs in normally. That closes the denial of service completely — but it also hands
/// back something the account-wide counter was good at. A caller who brings a thousand addresses
/// now gets five guesses each instead of five in total.
/// </para>
/// <para>
/// The account-wide counter therefore survives, at a far higher threshold, and it does <em>not</em>
/// deny anyone. Crossing it means "this account is under distributed attack" and its only effect is
/// to shrink the per-address allowance — five drops to one. A thousand addresses then buy a thousand
/// guesses rather than five thousand, while an address with no failures against it is still let
/// through on the first correct password. That asymmetry is the whole point: **the tightened state
/// can never deny a caller who has not personally failed**, so it cannot be turned back into a
/// denial-of-service.
/// </para>
/// <para>
/// The cost is real and bounded: while an account is under attack, a Driver who mistypes their own
/// password once is refused for the window instead of getting five tries. That is a degradation
/// during an attack, not a steady-state rule, and the owner is emailed when it starts.
/// </para>
/// <para>
/// What this type deliberately does not decide is what the caller is <em>told</em>. Sign-in has
/// exactly two outcomes, <c>200</c> and a generic <c>401</c>; a refusal here is indistinguishable
/// from a wrong password, an unconfirmed address, and an address with no account at all
/// (GHSA-28pc-cx5w-g6jp). Nothing in this file returns a reason, and a caller has nowhere to put
/// one.
/// </para>
/// </remarks>
public static class SignInThrottle
{
    /// <summary>Failures one source address may accumulate against one account before it is refused.</summary>
    public const int DefaultPerAddressMaxFailures = 5;

    /// <summary>
    /// The per-address allowance once an account is under distributed attack. One, not zero: zero
    /// would deny every address including the Driver's own, which is the denial of service this
    /// design exists to remove.
    /// </summary>
    public const int TightenedPerAddressMaxFailures = 1;

    /// <summary>
    /// Account-wide failures, across every source address, that mark an account as under attack.
    /// Far above any plausible run of fat-fingering and far below a useful guessing run.
    /// </summary>
    public const int DefaultAccountHighWaterFailures = 50;

    /// <summary>How long a source address's failures are held against it.</summary>
    public static readonly TimeSpan DefaultPerAddressWindow = TimeSpan.FromMinutes(15);

    /// <summary>How long account-wide failures accumulate before the high-water reading resets.</summary>
    public static readonly TimeSpan DefaultAccountWindow = TimeSpan.FromHours(1);

    /// <summary>
    /// Shortest gap between two "something is guessing at your account" emails for one account.
    /// </summary>
    /// <remarks>
    /// A rate limit, not a preference. Sign-in is unauthenticated, so a notice sent per lockout is a
    /// notice a stranger can trigger — once per source address, from as many as they like. One per
    /// account per interval keeps the warning useful and keeps the mailbox out of reach.
    /// </remarks>
    public static readonly TimeSpan DefaultNoticeInterval = TimeSpan.FromHours(1);

    /// <summary>The default policy — the numbers the API runs unless configuration overrides them.</summary>
    public static readonly SignInThrottleOptions Defaults = new(
        PerAddressMaxFailures: DefaultPerAddressMaxFailures,
        TightenedPerAddressMaxFailures: TightenedPerAddressMaxFailures,
        AccountHighWaterFailures: DefaultAccountHighWaterFailures,
        PerAddressWindow: DefaultPerAddressWindow,
        AccountWindow: DefaultAccountWindow,
        NoticeInterval: DefaultNoticeInterval);

    /// <summary>
    /// Whether a recorded window is still current, i.e. whether its failures still count.
    /// </summary>
    /// <remarks>
    /// A window that has run out is treated as absent rather than cleared, so this stays a pure
    /// read. The row is only rewritten when the next failure actually arrives.
    /// </remarks>
    public static bool IsCurrent(FailureWindow window, DateTimeOffset now, TimeSpan length) =>
        now < window.StartedAt + length;

    /// <summary>Failures still counting against a window, or zero once it has run out.</summary>
    public static int LiveCount(FailureWindow? window, DateTimeOffset now, TimeSpan length) =>
        window is { } w && IsCurrent(w, now, length) ? w.Count : 0;

    /// <summary>
    /// Decides whether one more attempt is allowed, and on what allowance.
    /// </summary>
    /// <param name="addressWindow">This source address's failures against this account, if any.</param>
    /// <param name="accountWindow">The account's failures across every address, if any.</param>
    /// <param name="now">Current time.</param>
    /// <param name="options">The policy in force.</param>
    public static ThrottleDecision Evaluate(
        FailureWindow? addressWindow,
        FailureWindow? accountWindow,
        DateTimeOffset now,
        SignInThrottleOptions options)
    {
        var accountFailures = LiveCount(accountWindow, now, options.AccountWindow);
        var underAttack = accountFailures >= options.AccountHighWaterFailures;

        var allowance = underAttack
            ? options.TightenedPerAddressMaxFailures
            : options.PerAddressMaxFailures;

        var addressFailures = LiveCount(addressWindow, now, options.PerAddressWindow);

        return new ThrottleDecision(
            Refused: addressFailures >= allowance,
            Allowance: allowance,
            UnderAttack: underAttack);
    }

    /// <summary>
    /// The window that replaces <paramref name="existing"/> after one more failure.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="FailureWindow.StartedAt"/> is carried forward untouched while the window is
    /// current, and only reset when a failure arrives after it has run out. That is deliberate and
    /// load-bearing: a window that restarted on every failure could be held open forever by a caller
    /// who kept failing, which is exactly how the account-wide lockout this replaces could be made
    /// permanent. Here a window always ends a fixed span after the failure that opened it, whatever
    /// arrives in between.
    /// </para>
    /// <para>
    /// The caller is expected not to record a failure at all while <see cref="ThrottleDecision.Refused"/>
    /// — there is no information in an attempt that was never checked. This rule is the second line:
    /// even a caller that recorded one anyway could not extend the window by doing so.
    /// </para>
    /// </remarks>
    public static FailureWindow RecordFailure(
        FailureWindow? existing,
        DateTimeOffset now,
        TimeSpan length) =>
        existing is { } w && IsCurrent(w, now, length)
            ? w with { Count = w.Count + 1 }
            : new FailureWindow(1, now);
}

/// <summary>
/// A count of failures and the instant the window holding them opened. Kept as a value so the
/// decision rules can be exercised without a row.
/// </summary>
public readonly record struct FailureWindow(int Count, DateTimeOffset StartedAt);

/// <summary>The verdict on one attempt.</summary>
/// <param name="Refused">
/// Whether the attempt is refused before the password is checked. Carries no reason: the caller has
/// nowhere to report one.
/// </param>
/// <param name="Allowance">Failures this source address is permitted in the current window.</param>
/// <param name="UnderAttack">
/// Whether the account-wide high-water mark is currently crossed. Reported for logging and for the
/// owner's notice, never to the caller.
/// </param>
public readonly record struct ThrottleDecision(bool Refused, int Allowance, bool UnderAttack);

/// <summary>The tunable half of the policy. Bound from configuration; see <see cref="SignInThrottle.Defaults"/>.</summary>
public sealed record SignInThrottleOptions(
    int PerAddressMaxFailures,
    int TightenedPerAddressMaxFailures,
    int AccountHighWaterFailures,
    TimeSpan PerAddressWindow,
    TimeSpan AccountWindow,
    TimeSpan NoticeInterval)
{
    /// <summary>
    /// Rejects a policy that would deny sign-in rather than throttle it. Call at startup.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are configuration-driven, and two of them turn a single mistyped character into the
    /// vulnerability this design removes:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <see cref="TightenedPerAddressMaxFailures"/> of <c>0</c> refuses every caller of a tightened
    /// account — including one with no failures at all, which is the account owner.
    /// </description></item>
    /// <item><description>
    /// <see cref="AccountHighWaterFailures"/> of <c>0</c> reads as "under attack" for every account
    /// in the system, permanently, so with the above it locks every Driver out of the product.
    /// </description></item>
    /// </list>
    /// <para>
    /// Neither produces an error on its own — the app would boot and quietly deny everyone. A comment
    /// in <c>.env.example</c> is not enough for an invariant whose whole point is "this must never
    /// deny anyone", so it is checked here and fails the host the way a short signing key does.
    /// </para>
    /// </remarks>
    public SignInThrottleOptions Validated()
    {
        if (TightenedPerAddressMaxFailures < 1)
            throw new InvalidOperationException(
                $"Sign-in throttle: the tightened per-address allowance must be at least 1, "
                + $"but is {TightenedPerAddressMaxFailures}. Zero would refuse the account owner too, "
                + "turning the throttle into the denial of service it exists to prevent.");

        if (PerAddressMaxFailures < TightenedPerAddressMaxFailures)
            throw new InvalidOperationException(
                $"Sign-in throttle: the ordinary per-address allowance ({PerAddressMaxFailures}) must not "
                + $"be below the tightened one ({TightenedPerAddressMaxFailures}); an account under attack "
                + "would otherwise be more permissive than one that is not.");

        if (AccountHighWaterFailures < 1)
            throw new InvalidOperationException(
                $"Sign-in throttle: the account high-water mark must be at least 1, but is "
                + $"{AccountHighWaterFailures}. Zero reads as 'under attack' for every account, always.");

        foreach (var (name, span) in new[]
                 {
                     (nameof(PerAddressWindow), PerAddressWindow),
                     (nameof(AccountWindow), AccountWindow),
                     (nameof(NoticeInterval), NoticeInterval),
                 })
        {
            if (span <= TimeSpan.Zero)
                throw new InvalidOperationException(
                    $"Sign-in throttle: {name} must be greater than zero, but is {span}.");
        }

        return this;
    }
}
