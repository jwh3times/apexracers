using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

/// <summary>
/// Resolves the authenticated user's iRacing customer id from the database. Returns
/// null when the user has no linked iRacing account; controllers turn that into a
/// typed 409 (see <c>ControllerExtensions.IRacingNotLinked</c>) so the client can
/// prompt the user to link rather than showing an empty result or a generic error.
/// </summary>
public class MemberContext(AppDbContext db)
{
    public async Task<long?> GetCustIdAsync(Guid userId, CancellationToken ct) =>
        await db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.IRacingCustomerId)
            .FirstOrDefaultAsync(ct);
}
