using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

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
    SignInThrottleOptions options)
{
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

    /// <summary>Decides whether one more attempt from <paramref name="address"/> is allowed.</summary>
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
    /// Records one failed attempt and reports whether the account's owner should be told that
    /// something is guessing at their account.
    /// </summary>
    /// <remarks>
    /// The notice fires when this failure exhausts a source address's allowance, or while the
    /// account is over its high-water mark — the two states that mean "this is no longer someone
    /// mistyping". At most one request per account per
    /// <see cref="SignInThrottleOptions.NoticeInterval"/> ever gets a <c>true</c> back, however many
    /// addresses are failing; see <see cref="ClaimNoticeAsync"/>.
    /// </remarks>
    /// <returns><c>true</c> for the one caller entitled to send the email.</returns>
    public async Task<bool> RecordFailureAsync(Guid userId, string address, CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();

        await IncrementAddressAsync(userId, address, now, ct);
        await IncrementAccountAsync(userId, now, ct);

        // Re-read rather than infer from the increments: they are separate statements and a
        // concurrent failure may have moved either count. This is the failure path, not the hot one.
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

    private async Task IncrementAddressAsync(Guid userId, string address, DateTimeOffset now, CancellationToken ct)
    {
        var windowFloor = now - options.PerAddressWindow;

        // Current window: count up, and leave WindowStartedAt exactly where it is. That is what stops
        // a caller who keeps failing from holding their own window open indefinitely.
        var bumped = await db.SignInAddressFailures
            .Where(f => f.UserId == userId && f.IpAddress == address && f.WindowStartedAt > windowFloor)
            .ExecuteUpdateAsync(
                s => s.SetProperty(f => f.FailureCount, f => f.FailureCount + 1)
                      .SetProperty(f => f.LastFailureAt, now),
                ct);
        if (bumped > 0)
            return;

        // Row exists but its window has run out: reopen it from now.
        //
        // The expiry test in the predicate is load-bearing, not a repeat of the check above. Without
        // it this statement resets FailureCount to 1 for ANY existing row — so a request whose
        // increment missed because no row existed yet could land this reset after a concurrent
        // request inserted a live one, wiping every failure accumulated in between. A guesser racing
        // their own requests could then sit at a count of 1 for ever and never be throttled.
        var reopened = await db.SignInAddressFailures
            .Where(f => f.UserId == userId && f.IpAddress == address && f.WindowStartedAt <= windowFloor)
            .ExecuteUpdateAsync(
                s => s.SetProperty(f => f.FailureCount, 1)
                      .SetProperty(f => f.WindowStartedAt, now)
                      .SetProperty(f => f.LastFailureAt, now),
                ct);
        if (reopened > 0)
            return;

        await InsertOrRetryAsync(
            () => db.SignInAddressFailures.Add(new SignInAddressFailure
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                IpAddress = address,
                FailureCount = 1,
                WindowStartedAt = now,
                LastFailureAt = now,
            }),
            retry: () => db.SignInAddressFailures
                .Where(f => f.UserId == userId && f.IpAddress == address)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(f => f.FailureCount, f => f.FailureCount + 1)
                          .SetProperty(f => f.LastFailureAt, now),
                    ct),
            ct);
    }

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
            ct);
    }

    /// <summary>
    /// Inserts a first row, falling back to an increment when a concurrent request inserted it first.
    /// </summary>
    /// <remarks>
    /// Two requests can both find no row and both try to insert; the unique index lets exactly one
    /// through. The loser must not drop its failure on the floor — that would be a free attempt — so
    /// it re-applies itself as an increment against the row the winner created.
    /// </remarks>
    private async Task InsertOrRetryAsync(Action add, Func<Task<int>> retry, CancellationToken ct)
    {
        add();
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            foreach (var entry in db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added).ToList())
                entry.State = EntityState.Detached;

            await retry();
        }
    }
}
