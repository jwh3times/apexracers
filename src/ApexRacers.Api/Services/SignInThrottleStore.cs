using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ApexRacers.Api.Services;

/// <summary>
/// The persistence half of sign-in throttling: reads the counters
/// <see cref="SignInThrottle"/> decides on, and records failures against them.
/// </summary>
/// <remarks>
/// <para>
/// Every write here is an atomic statement rather than a read-modify-write. Sign-in is reachable by
/// anyone, so concurrent failures against one account are not an edge case an attacker has to work
/// for — they are the attack. A lost increment favours the guesser, so counting has to be done by
/// the database.
/// </para>
/// <para>
/// The one write that is <em>not</em> just a counter is the owner's notice flag, and it is the
/// reason this is worth care: setting it conditionally means exactly one request can ever observe
/// the transition, so an attack produces one email rather than one per failed guess. An
/// unauthenticated endpoint that could send an email per request would be a way to flood a Driver's
/// inbox — a worse denial of service than the one this whole change exists to remove.
/// </para>
/// </remarks>
public sealed class SignInThrottleStore(
    AppDbContext db,
    TimeProvider timeProvider,
    SignInThrottleOptions options,
    ILogger<SignInThrottleStore> logger)
{
    /// <summary>Primary key behind the one-row-per-account rule.</summary>
    private const string AccountPrimaryKey = "PK_SignInAccountFailures";

    /// <summary>
    /// Stand-in address for a request that arrived with none. Grouping these together is
    /// deliberate: it is a single shared bucket, so an unattributable caller cannot mint
    /// itself a fresh allowance by being unattributable.
    /// </summary>
    public const string UnknownAddress = "unknown";

    /// <summary>Normalises what the request reported into the value stored against a row.</summary>
    public static string NormaliseAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return UnknownAddress;

        var trimmed = address.Trim();
        // The column is sized for an IPv6 address with a scope id. Anything longer did not come from
        // the proxy, so it is bucketed rather than stored — truncating could collide two real
        // addresses into one allowance.
        return trimmed.Length > 56 ? UnknownAddress : trimmed;
    }

    /// <summary>
    /// Claims one attempt for <paramref name="address"/> and says whether it may proceed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This exists because reading a counter, checking a password, and then writing the counter is
    /// three steps with two gaps, and a caller does not have to be clever to get inside them — the
    /// password check is tens of milliseconds of PBKDF2, which is an enormous window. Fire the whole
    /// per-minute rate-limit budget at once and every request reads the same count, every request
    /// passes the gate, and every password is checked. The allowance silently becomes
    /// <c>max(allowance, requests in flight)</c>.
    /// </para>
    /// <para>
    /// That matters most exactly where the design leans hardest: under attack the allowance is 1, so
    /// a concurrent burst would buy ten guesses per address instead of one and the tightened state
    /// would be worth a tenth of what it claims.
    /// </para>
    /// <para>
    /// So the count the gate reads is the one this request just wrote. A single statement inserts,
    /// increments, or reopens the window and returns the resulting value, so N concurrent callers get
    /// N distinct numbers and only the first <c>allowance</c> of them proceed. It also means an
    /// attempt is claimed <em>before</em> the password is checked rather than recorded after it
    /// fails — a correct password clears the row immediately afterwards, so an honest caller is
    /// unaffected unless they sign in many times concurrently, which is not a thing sign-in does.
    /// </para>
    /// <para>
    /// Claiming before the check does not let anyone hold a window open: the statement preserves
    /// <c>WindowStartedAt</c> whenever the window is still current and only resets it once the window
    /// has actually run out, so the window always ends a fixed span after the attempt that opened it,
    /// however many arrive in between.
    /// </para>
    /// </remarks>
    public async Task<ThrottleDecision> ClaimAttemptAsync(Guid userId, string address, CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();
        var windowFloor = now - options.PerAddressWindow;

        // One statement: insert, or bump within the live window, or reopen an expired one — and hand
        // back the count it settled on. EF has no upsert, and splitting this into read + write is the
        // very race the remarks above describe, so it is written as SQL deliberately.
        var claimedRows = await db.Database
            .SqlQueryRaw<int>(
                """
                INSERT INTO identity."SignInAddressFailures"
                    ("Id", "UserId", "IpAddress", "FailureCount", "WindowStartedAt", "LastFailureAt")
                VALUES ({0}, {1}, {2}, 1, {3}, {3})
                ON CONFLICT ("UserId", "IpAddress") DO UPDATE SET
                    "FailureCount" = CASE
                        WHEN "SignInAddressFailures"."WindowStartedAt" > {4}
                        THEN "SignInAddressFailures"."FailureCount" + 1
                        ELSE 1
                    END,
                    "WindowStartedAt" = CASE
                        WHEN "SignInAddressFailures"."WindowStartedAt" > {4}
                        THEN "SignInAddressFailures"."WindowStartedAt"
                        ELSE {3}
                    END,
                    "LastFailureAt" = {3}
                RETURNING "FailureCount" AS "Value"
                """,
                Guid.NewGuid(), userId, address, now, windowFloor)
            // Enumerated, not composed. An INSERT ... RETURNING is not composable, so asking EF for
            // Single() would have it wrap the statement in a subquery and the provider rejects it.
            .ToListAsync(ct);
        var claimed = claimedRows.Single();

        var accountRow = await db.SignInAccountFailures
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.UserId == userId, ct);

        var underAttack = SignInThrottle.LiveCount(
            accountRow is null ? null : new FailureWindow(accountRow.FailureCount, accountRow.WindowStartedAt),
            now,
            options.AccountWindow) >= options.AccountHighWaterFailures;

        var allowance = underAttack
            ? options.TightenedPerAddressMaxFailures
            : options.PerAddressMaxFailures;

        // Strictly greater: this request's own claim is already counted, so a claim landing exactly on
        // the allowance is the last one permitted.
        return new ThrottleDecision(Refused: claimed > allowance, Allowance: allowance, UnderAttack: underAttack);
    }

    /// <summary>Reads the current decision without claiming an attempt. For tests and diagnostics.</summary>
    public async Task<ThrottleDecision> EvaluateAsync(Guid userId, string address, CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();

        var addressRow = await db.SignInAddressFailures
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.UserId == userId && f.IpAddress == address, ct);

        var accountRow = await db.SignInAccountFailures
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.UserId == userId, ct);

        return SignInThrottle.Evaluate(
            addressRow is null ? null : new FailureWindow(addressRow.FailureCount, addressRow.WindowStartedAt),
            accountRow is null ? null : new FailureWindow(accountRow.FailureCount, accountRow.WindowStartedAt),
            now,
            options);
    }

    /// <summary>
    /// Notes that a claimed attempt turned out to be wrong, and reports whether the account's owner
    /// should be told that something is guessing at their account.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The source address was already counted by <see cref="ClaimAttemptAsync"/> before the password
    /// was checked; this adds the account-wide half, which is deliberately counted only for attempts
    /// that actually failed. Counting claimed-but-correct attempts there would let ordinary sign-ins
    /// push an account towards "under attack".
    /// </para>
    /// <para>
    /// The notice fires when the address has exhausted its allowance, or while the account is over
    /// its high-water mark — the two states that mean "this is no longer someone mistyping". At most
    /// one request per account per <see cref="SignInThrottleOptions.NoticeInterval"/> ever gets a
    /// <c>true</c> back, however many addresses are failing; see <see cref="ClaimNoticeAsync"/>.
    /// </para>
    /// </remarks>
    /// <returns><c>true</c> for the one caller entitled to send the email.</returns>
    public async Task<bool> NoteFailureAsync(Guid userId, string address, CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();

        await IncrementAccountAsync(userId, now, ct);

        var after = await EvaluateAsync(userId, address, ct);
        if (!after.Refused && !after.UnderAttack)
            return false;

        return await ClaimNoticeAsync(userId, now, ct);
    }

    /// <summary>
    /// Claims the right to send this account's next security notice, if one is due.
    /// </summary>
    /// <remarks>
    /// The interval is enforced by the predicate, so the database picks exactly one winner when
    /// several failures arrive together. Doing this with a read-then-write would let a burst from a
    /// hundred addresses each decide it was first and send a hundred emails — which is the flood
    /// the interval exists to prevent.
    /// </remarks>
    private async Task<bool> ClaimNoticeAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var noticeFloor = now - options.NoticeInterval;

        var claimed = await db.SignInAccountFailures
            .Where(f => f.UserId == userId
                        && (f.NoticeSentAt == null || f.NoticeSentAt < noticeFloor))
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.NoticeSentAt, now), ct);

        return claimed > 0;
    }

    /// <summary>
    /// Clears what one source address has against an account, after it proved it holds the password.
    /// </summary>
    /// <remarks>
    /// The account-wide counter is deliberately left alone. It records that an attack is in
    /// progress, and the owner succeeding from their own machine is not evidence that it stopped —
    /// clearing it there would let an attacker's pressure be reset by the very person they are
    /// attacking. It expires on its own once the failures actually stop.
    /// </remarks>
    public async Task ClearAddressAsync(Guid userId, string address, CancellationToken ct = default) =>
        await db.SignInAddressFailures
            .Where(f => f.UserId == userId && f.IpAddress == address)
            .ExecuteDeleteAsync(ct);

    private async Task IncrementAccountAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var windowFloor = now - options.AccountWindow;

        var bumped = await db.SignInAccountFailures
            .Where(f => f.UserId == userId && f.WindowStartedAt > windowFloor)
            .ExecuteUpdateAsync(
                s => s.SetProperty(f => f.FailureCount, f => f.FailureCount + 1)
                      .SetProperty(f => f.LastFailureAt, now),
                ct);
        if (bumped > 0)
            return;

        // Same expiry test, for the same reason as the address row: without it a racing request can
        // reset a live counter to 1 and erase the failures already counted against the account.
        //
        // NoticeSentAt is deliberately NOT cleared when the window reopens. It paces the owner's
        // email on its own interval, and resetting it here would let a caller who waits out the
        // account window earn a fresh email every time.
        var reopened = await db.SignInAccountFailures
            .Where(f => f.UserId == userId && f.WindowStartedAt <= windowFloor)
            .ExecuteUpdateAsync(
                s => s.SetProperty(f => f.FailureCount, 1)
                      .SetProperty(f => f.WindowStartedAt, now)
                      .SetProperty(f => f.LastFailureAt, now),
                ct);
        if (reopened > 0)
            return;

        await InsertOrRetryAsync(
            () => db.SignInAccountFailures.Add(new SignInAccountFailure
            {
                UserId = userId,
                FailureCount = 1,
                WindowStartedAt = now,
                LastFailureAt = now,
                NoticeSentAt = null,
            }),
            retry: () => db.SignInAccountFailures
                .Where(f => f.UserId == userId)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(f => f.FailureCount, f => f.FailureCount + 1)
                          .SetProperty(f => f.LastFailureAt, now),
                    ct),
            AccountPrimaryKey,
            ct);
    }

    /// <summary>
    /// Inserts a first row, falling back to an increment when a concurrent request inserted it first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two requests can both find no row and both try to insert; the unique index lets exactly one
    /// through. The loser must not drop its failure on the floor — that would be a free attempt — so
    /// it re-applies itself as an increment against the row the winner created.
    /// </para>
    /// <para>
    /// The catch is matched to a **unique violation on the expected constraint**, not to
    /// <see cref="DbUpdateException"/> at large. Treating every save failure as a lost race would
    /// mean a transient fault — the sort this store exists to survive, since concurrent failures
    /// against one account are the attack rather than an edge case — got silently relabelled as
    /// "someone else inserted it", followed by an update that matches nothing. The failure would
    /// vanish with no row, no exception and no log: a free attempt for whoever caused the fault.
    /// Anything else propagates. Mirrors the narrow catch in <c>AuthService</c>'s Claimed Identity
    /// conflict.
    /// </para>
    /// </remarks>
    private async Task InsertOrRetryAsync(
        Action add,
        Func<Task<int>> retry,
        string constraintName,
        CancellationToken ct)
    {
        add();
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
            } pg
            && pg.ConstraintName == constraintName)
        {
            foreach (var entry in db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added).ToList())
                entry.State = EntityState.Detached;

            // Zero here would mean the row the violation proved existed has since gone — only a purge
            // racing this exact window could do it, and the sweep stays a full grace period clear of
            // live rows. Logged rather than thrown: this runs on a request that is about to be
            // refused anyway, and throwing would answer a wrong password with a 500 for real accounts
            // only, which is an account oracle.
            if (await retry() == 0)
                logger.LogWarning(
                    "Sign-in failure was not recorded: {Constraint} reported a duplicate but no row remained.",
                    constraintName);
        }
    }
}
