using ApexRacers.Api.Dtos;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

/// <summary>
/// Manages the rivals a user follows for head-to-head comparison: persistence (add/list/remove),
/// driver name-search (via iRacing), and suggestions drawn from drivers the caller has raced.
/// </summary>
public class RivalService(AppDbContext db, CachedIRacingClient cached)
{
    private const int MaxSuggestions = 12;
    private static readonly TimeSpan SearchTtl = TimeSpan.FromMinutes(30);

    public async Task<IReadOnlyList<RivalDto>> ListAsync(Guid userId, CancellationToken ct) =>
        await db.Rivals
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new RivalDto(r.RivalCustId, r.DisplayName, r.CreatedAt))
            .ToListAsync(ct);

    /// <summary>Idempotent: re-adding an existing (user, rival) pair returns the original entry.</summary>
    public async Task<RivalDto> AddAsync(Guid userId, long custId, string displayName, CancellationToken ct)
    {
        if (custId <= 0)
            throw new ArgumentException("A valid iRacing customer id is required.", nameof(custId));

        var existing = await db.Rivals
            .FirstOrDefaultAsync(r => r.UserId == userId && r.RivalCustId == custId, ct);
        if (existing is not null)
            return new RivalDto(existing.RivalCustId, existing.DisplayName, existing.CreatedAt);

        var name = displayName?.Trim() is { Length: > 0 } n ? n : $"Driver {custId}";
        var rival = new Rival
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RivalCustId = custId,
            DisplayName = name,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Rivals.Add(rival);
        await db.SaveChangesAsync(ct);
        return new RivalDto(rival.RivalCustId, rival.DisplayName, rival.CreatedAt);
    }

    public async Task RemoveAsync(Guid userId, long custId, CancellationToken ct)
    {
        var rival = await db.Rivals
            .FirstOrDefaultAsync(r => r.UserId == userId && r.RivalCustId == custId, ct);
        if (rival is null) return;
        db.Rivals.Remove(rival);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Name search via iRacing, cached per normalized term. Short terms skip the API.</summary>
    public async Task<IReadOnlyList<DriverSearchResultDto>> SearchDriversAsync(string term, CancellationToken ct)
    {
        var normalized = (term ?? string.Empty).Trim();
        if (normalized.Length < 2) return [];

        return await cached.GetOrFetchAsync(
            $"driversearch:{normalized.ToLowerInvariant()}", SearchTtl,
            async c =>
            {
                var hits = (await c.SearchDriversAsync(normalized, null, ct)).Data ?? [];
                return hits
                    .Select(d => new DriverSearchResultDto(d.CustomerId, d.DisplayName ?? string.Empty))
                    .ToList();
            },
            ct);
    }

    /// <summary>
    /// Drivers the caller has raced (from ingested subsession results), ranked by how many races
    /// they share. Excludes the caller and anyone already followed.
    /// </summary>
    public async Task<IReadOnlyList<RivalSuggestionDto>> SuggestionsAsync(
        Guid userId, long callerCustId, CancellationToken ct)
    {
        var mySubIds = db.SubsessionResults
            .Where(r => r.CustId == callerCustId)
            .Select(r => r.SubsessionId);
        var followed = db.Rivals
            .Where(r => r.UserId == userId)
            .Select(r => r.RivalCustId);

        var raw = await db.SubsessionResults
            .Where(r => r.CustId != callerCustId
                && mySubIds.Contains(r.SubsessionId)
                && !followed.Contains(r.CustId))
            .GroupBy(r => r.CustId)
            .Select(g => new { CustId = g.Key, Name = g.Max(x => x.DisplayName), Shared = g.Count() })
            .ToListAsync(ct);

        return raw
            .OrderByDescending(x => x.Shared)
            .ThenBy(x => x.Name, StringComparer.Ordinal)
            .Take(MaxSuggestions)
            .Select(x => new RivalSuggestionDto(x.CustId, x.Name ?? string.Empty, x.Shared))
            .ToList();
    }
}
