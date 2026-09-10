using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

/// <summary>
/// Owns the complete refresh-token lifecycle: issuance, rotation, revocation, active-token
/// capping, and retention cleanup. Raw credentials leave this module only as return values and
/// are never attached to a persisted entity.
/// </summary>
public sealed class RefreshTokenStore(
    AppDbContext db,
    TimeProvider timeProvider,
    ILogger<RefreshTokenStore> logger)
{
    private const int RefreshTokenDays = 7;
    private const int MaxActiveTokensPerUser = 5;

    /// <summary>
    /// The single rejection every unusable-credential path returns. Unknown, expired, replayed, and
    /// race-losing tokens are deliberately indistinguishable to the caller.
    /// </summary>
    private const string InvalidTokenMessage = "Invalid or expired refresh token.";

    /// <summary>
    /// How many times revocation re-sweeps before giving up. Two passes settle every reachable
    /// case — one to revoke what is there, one to catch a successor committed behind the first —
    /// because a third generation would require a client to have received and spent the second,
    /// which cannot happen inside the window. The extra pass is slack, not expected work.
    /// </summary>
    private const int MaxRevocationPasses = 4;

    public async Task<string> IssueAsync(Guid userId, CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();
        await RevokeTokensBeyondCapAsync(userId, now, ct);

        var issued = CreateToken(userId, now);
        db.RefreshTokens.Add(issued.Entity);
        await db.SaveChangesAsync(ct);
        return issued.RawToken;
    }

    /// <summary>
    /// Consumes a refresh token and issues its single successor.
    /// </summary>
    /// <remarks>
    /// The read below decides whether the credential <em>looks</em> usable; it is deliberately not
    /// what decides the caller wins. Two requests presenting the same token can both read it while
    /// <c>RevokedAt</c> is still null, so consumption is a conditional update — the database, not
    /// the read, picks exactly one winner (GHSA-87m2-6r5g-9q47). The loser is told only that the
    /// token is invalid.
    /// </remarks>
    public async Task<RefreshTokenRotation> RotateAsync(
        string rawToken,
        CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();
        var hash = HashToken(rawToken);
        var stored = await db.RefreshTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(token => token.TokenHash == hash, ct)
            ?? throw new InvalidOperationException(InvalidTokenMessage);

        if (stored.RevokedAt is not null)
        {
            await RevokeAllActiveAsync(stored.UserId, ct);
            logger.LogWarning("Refresh-token reuse detected for user {UserId}.", stored.UserId);
            throw new InvalidOperationException(InvalidTokenMessage);
        }

        if (stored.ExpiresAt <= now)
            throw new InvalidOperationException(InvalidTokenMessage);

        // The revoke and the insert must still land together, which an explicit transaction is now
        // what provides: the conditional consume runs as its own statement, so a single
        // SaveChanges no longer spans both. A failed insert must leave the credential spendable.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var consumed = await db.RefreshTokens
            .Where(token => token.Id == stored.Id && token.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.RevokedAt, now),
                ct);

        // Zero rows means a concurrent rotation consumed this credential first. Under READ
        // COMMITTED the statement above blocks on that writer and re-evaluates its predicate after
        // the winner commits, so exactly one racer can ever see a row here.
        if (consumed == 0)
            throw new InvalidOperationException(InvalidTokenMessage);

        // Losing the race is NOT replay: two tabs share one credential through the client's store
        // while its single-flight guard is per tab, so honest clients rotate concurrently. Calling
        // this reuse would sign a user out everywhere for refreshing twice at once. Replay is still
        // caught above, on the next presentation of a credential already spent.
        //
        // Rotation is deliberately cap-exempt: it replaces one active credential with one active
        // credential.
        var replacement = CreateToken(stored.UserId, now);
        db.RefreshTokens.Add(replacement.Entity);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new RefreshTokenRotation(stored.UserId, replacement.RawToken);
    }

    /// <summary>
    /// Best-effort revocation of a specifically presented credential. Unlike an active-token
    /// query, an expired but not-yet-revoked row may still be stamped revoked; unknown and already
    /// revoked credentials are no-ops.
    /// </summary>
    public async Task RevokeAsync(string rawToken, CancellationToken ct = default)
    {
        var hash = HashToken(rawToken);
        var stored = await db.RefreshTokens
            .FirstOrDefaultAsync(token => token.TokenHash == hash, ct);

        if (stored is null or { RevokedAt: not null })
            return;

        stored.RevokedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Ends every session the user currently holds.
    /// </summary>
    /// <remarks>
    /// Sweeping once is not enough, and the gap is not the obvious one. A rotation that commits
    /// while this is between reading the user's active tokens and writing them adds a credential
    /// the sweep never saw — and since nothing revisits it, that successor outlives the very
    /// revocation meant to end it. Each pass is its own statement and therefore sees what the
    /// previous one could not, so this repeats until a pass finds nothing left to revoke.
    /// Convergence, not a single sweep, is what makes the revocation boundary hold.
    /// </remarks>
    public async Task RevokeAllActiveAsync(Guid userId, CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();

        for (var pass = 1; pass <= MaxRevocationPasses; pass++)
        {
            var revoked = await db.RefreshTokens
                .Where(token => token.UserId == userId)
                .Where(ActiveAt(now))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(token => token.RevokedAt, now),
                    ct);

            if (revoked == 0)
                return;
        }

        // Reaching here means credentials kept appearing faster than they could be revoked, which no
        // ordinary client can cause: a successor can only be minted by spending the credential it
        // replaces, and this has already revoked those. Left as a warning rather than a throw —
        // callers treat revocation as cleanup on a path that is itself about to reject the request,
        // and swapping in a different exception would change what the caller reports.
        logger.LogWarning(
            "Refresh-token revocation for user {UserId} did not converge in {Passes} passes.",
            userId,
            MaxRevocationPasses);
    }

    public async Task<int> PurgeExpiredAsync(
        TimeSpan retention,
        CancellationToken ct = default)
    {
        var cutoff = timeProvider.GetUtcNow() - retention;
        var expired = await db.RefreshTokens
            .Where(token => token.ExpiresAt < cutoff)
            .ToListAsync(ct);

        if (expired.Count == 0)
            return 0;

        db.RefreshTokens.RemoveRange(expired);
        await db.SaveChangesAsync(ct);
        return expired.Count;
    }

    private async Task RevokeTokensBeyondCapAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var active = await db.RefreshTokens
            .Where(token => token.UserId == userId)
            .Where(ActiveAt(now))
            .OrderBy(token => token.CreatedAt)
            .ToListAsync(ct);

        var excess = active.Count - (MaxActiveTokensPerUser - 1);
        for (var i = 0; i < excess; i++)
            active[i].RevokedAt = now;
    }

    private static Expression<Func<RefreshToken, bool>> ActiveAt(DateTimeOffset now) =>
        token => token.RevokedAt == null && token.ExpiresAt > now;

    private static IssuedRefreshToken CreateToken(Guid userId, DateTimeOffset now)
    {
        var bytes = new byte[64];
        RandomNumberGenerator.Fill(bytes);
        var rawToken = Convert.ToBase64String(bytes);
        return new IssuedRefreshToken(
            rawToken,
            new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TokenHash = HashToken(rawToken),
                CreatedAt = now,
                ExpiresAt = now.AddDays(RefreshTokenDays),
            });
    }

    private static string HashToken(string rawToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed record IssuedRefreshToken(string RawToken, RefreshToken Entity);
}

public sealed record RefreshTokenRotation(Guid UserId, string RawToken);
