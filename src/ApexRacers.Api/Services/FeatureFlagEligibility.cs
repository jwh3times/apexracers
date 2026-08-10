using ApexRacers.Core.Models;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

/// <summary>
/// Owns the role hierarchy and the answer to whether a feature flag is active for a role or user.
/// Unknown and role-less users deliberately receive Standard eligibility.
/// </summary>
public class FeatureFlagEligibility(AppDbContext db)
{
    private static readonly Dictionary<string, int> RoleLevels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Standard"] = 0,
        ["Beta"] = 1,
        ["Alpha"] = 2,
        ["Admin"] = 3,
    };

    public static bool IsKnownRole(string role) => RoleLevels.ContainsKey(role);

    public async Task<List<FeatureFlag>> GetActiveForRoleAsync(
        string role,
        CancellationToken ct = default)
    {
        var flags = await db.FeatureFlags
            .Where(f => f.IsEnabled)
            .ToListAsync(ct);

        return flags.Where(f => IsEligible(role, f.MinimumRole)).ToList();
    }

    public async Task<List<FeatureFlag>> GetActiveForUserAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        var role = await RoleForUserAsync(userId, ct);
        return await GetActiveForRoleAsync(role, ct);
    }

    public async Task<bool> IsActiveForUserAsync(
        string key,
        Guid userId,
        CancellationToken ct = default)
    {
        var candidate = await db.FeatureFlags
            .Where(f => f.Key == key)
            .Select(f => new
            {
                f.IsEnabled,
                f.MinimumRole,
                Role = (
                    from userRole in db.UserRoles
                    where userRole.UserId == userId
                    join role in db.Roles on userRole.RoleId equals role.Id
                    select role.Name).FirstOrDefault(),
            })
            .FirstOrDefaultAsync(ct);

        return candidate is not null
            && candidate.IsEnabled
            && IsEligible(candidate.Role ?? "Standard", candidate.MinimumRole);
    }

    private async Task<string> RoleForUserAsync(Guid userId, CancellationToken ct) =>
        await (
            from userRole in db.UserRoles
            where userRole.UserId == userId
            join role in db.Roles on userRole.RoleId equals role.Id
            select role.Name).FirstOrDefaultAsync(ct) ?? "Standard";

    private static bool IsEligible(string role, string minimumRole) =>
        RoleLevels.TryGetValue(minimumRole, out var minimumLevel)
        && minimumLevel <= RoleLevels.GetValueOrDefault(role, 0);
}
