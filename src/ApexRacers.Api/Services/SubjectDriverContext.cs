using ApexRacers.Core;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

/// <summary>
/// Resolves the authenticated User's Subject Driver from the database. Optional
/// personalization uses <see cref="GetSubjectDriverCustIdAsync"/>; endpoints that require a
/// Subject Driver use <see cref="GetRequiredSubjectDriverCustIdAsync"/>, which owns the typed
/// 409 failure contract.
/// <para>
/// Demo override: when the <c>iracing-demo</c> flag is active for the caller's role,
/// every lookup resolves to the shared synthetic <see cref="DemoData.DriverCustId"/>
/// (real cust_ids have no backing data while iRacing creds are absent). This is the
/// only demo-aware branch in the API.
/// </para>
/// </summary>
public sealed record SubjectDriver(long CustomerId, DataProvenance Provenance);

public class SubjectDriverContext(AppDbContext db, FeatureFlagEligibility featureFlags, IRacingDataScope? dataScope = null)
{
    public async Task<SubjectDriver?> GetSubjectDriverAsync(Guid userId, CancellationToken ct = default)
    {
        if (dataScope is { IsSelected: true, Provenance: DataProvenance.Unknown })
            throw new IRacingNotConfiguredException();
        var demo = dataScope is { IsSelected: true }
            ? dataScope.Provenance == DataProvenance.Demo
            : await featureFlags.IsActiveForUserAsync("iracing-demo", userId, ct);
        if (demo) return new SubjectDriver(DemoData.DriverCustId, DataProvenance.Demo);

        var customerId = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.IRacingCustomerId)
            .FirstOrDefaultAsync(ct);
        return customerId is { } id ? new SubjectDriver(id, DataProvenance.Real) : null;
    }

    public async Task<long?> GetSubjectDriverCustIdAsync(Guid userId, CancellationToken ct = default) =>
        (await GetSubjectDriverAsync(userId, ct))?.CustomerId;

    public async Task<long> GetRequiredSubjectDriverCustIdAsync(
        Guid userId, CancellationToken ct = default)
    {
        var subjectDriverCustId = await GetSubjectDriverCustIdAsync(userId, ct);
        return RequireSubjectDriverCustId(subjectDriverCustId);
    }

    public long RequireSubjectDriverCustId(long? subjectDriverCustId) =>
        subjectDriverCustId is null or 0
            ? throw new IRacingNotLinkedException()
            : subjectDriverCustId.Value;
}
