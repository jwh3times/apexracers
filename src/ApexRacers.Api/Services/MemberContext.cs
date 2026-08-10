using ApexRacers.Core;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

/// <summary>
/// Resolves the authenticated user's iRacing customer id from the database. Optional
/// personalization uses <see cref="GetCustIdAsync"/>; endpoints that require a link use
/// <see cref="GetRequiredCustIdAsync"/>, which owns the typed 409 failure contract.
/// <para>
/// Demo override: when the <c>iracing-demo</c> flag is active for the caller's role,
/// every lookup resolves to the shared synthetic <see cref="DemoData.DriverCustId"/>
/// (real cust_ids have no backing data while iRacing creds are absent). This is the
/// only demo-aware branch in the API.
/// </para>
/// </summary>
public class MemberContext(AppDbContext db, FeatureFlagEligibility featureFlags)
{
    public async Task<long?> GetCustIdAsync(Guid userId, CancellationToken ct = default)
    {
        if (await featureFlags.IsActiveForUserAsync("iracing-demo", userId, ct))
            return DemoData.DriverCustId;

        return await db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.IRacingCustomerId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<long> GetRequiredCustIdAsync(Guid userId, CancellationToken ct = default)
    {
        var custId = await GetCustIdAsync(userId, ct);
        return RequireCustId(custId);
    }

    public long RequireCustId(long? custId) =>
        custId is null or 0 ? throw new IRacingNotLinkedException() : custId.Value;
}
