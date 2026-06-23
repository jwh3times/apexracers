using ApexRacers.Core;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

/// <summary>
/// Resolves the authenticated user's iRacing customer id from the database. Returns
/// null when the user has no linked iRacing account; controllers turn that into a
/// typed 409 (see <c>ControllerExtensions.IRacingNotLinked</c>) so the client can
/// prompt the user to link rather than showing an empty result or a generic error.
/// <para>
/// Demo override: when the <c>iracing-demo</c> flag is active for the caller's role,
/// every lookup resolves to the shared synthetic <see cref="DemoData.DriverCustId"/>
/// (real cust_ids have no backing data while iRacing creds are absent). This is the
/// only demo-aware branch in the API.
/// </para>
/// </summary>
public class MemberContext(AppDbContext db)
{
    public async Task<long?> GetCustIdAsync(Guid userId, CancellationToken ct)
    {
        if (await IsDemoActiveForUserAsync(userId, ct))
            return DemoData.DriverCustId;

        return await db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.IRacingCustomerId)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<bool> IsDemoActiveForUserAsync(Guid userId, CancellationToken ct)
    {
        var flag = await db.FeatureFlags.FirstOrDefaultAsync(f => f.Key == "iracing-demo", ct);
        if (flag is null || !flag.IsEnabled) return false;

        var roleName = await (
            from ur in db.UserRoles
            where ur.UserId == userId
            join r in db.Roles on ur.RoleId equals r.Id
            select r.Name).FirstOrDefaultAsync(ct) ?? "Standard";

        var userLevel = AdminService.RoleHierarchy.GetValueOrDefault(roleName, 0);
        var minLevel = AdminService.RoleHierarchy.GetValueOrDefault(flag.MinimumRole, 0);
        return userLevel >= minLevel;
    }
}
