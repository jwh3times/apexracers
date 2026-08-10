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
public sealed class RefreshTokenStore(AppDbContext db, TimeProvider timeProvider)
{
    private const int RefreshTokenDays = 7;
    private const int MaxActiveTokensPerUser = 5;

    public async Task<string> IssueAsync(Guid userId, CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();
        await RevokeTokensBeyondCapAsync(userId, now, ct);

        var issued = CreateToken(userId, now);
        db.RefreshTokens.Add(issued.Entity);
        await db.SaveChangesAsync(ct);
        return issued.RawToken;
    }

    public async Task<RefreshTokenRotation> RotateAsync(
        string rawToken,
        CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();
        var hash = HashToken(rawToken);
        var stored = await db.RefreshTokens
            .Where(ActiveAt(now))
            .FirstOrDefaultAsync(token => token.TokenHash == hash, ct)
            ?? throw new InvalidOperationException("Invalid or expired refresh token.");

        stored.RevokedAt = now;
        var replacement = CreateToken(stored.UserId, now);
        db.RefreshTokens.Add(replacement.Entity);

        // Rotation is deliberately cap-exempt: it replaces one active credential with one active
        // credential. The revoke and insert must remain in this single SaveChanges operation.
        await db.SaveChangesAsync(ct);
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

    public async Task RevokeAllActiveAsync(Guid userId, CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();
        var active = await db.RefreshTokens
            .Where(token => token.UserId == userId)
            .Where(ActiveAt(now))
            .ToListAsync(ct);

        if (active.Count == 0)
            return;

        foreach (var token in active)
            token.RevokedAt = now;
        await db.SaveChangesAsync(ct);
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
