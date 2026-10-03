using ApexRacers.Core;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

/// <summary>
/// Resolves the authenticated User's Subject Driver and provenance. Optional
/// personalization uses <see cref="GetSubjectDriverCustIdAsync"/>; endpoints that require a
/// Subject Driver use <see cref="GetRequiredSubjectDriverCustIdAsync"/>, which owns the typed
/// 409 failure contract.
/// <para>
/// The selected Demo request scope resolves to the shared synthetic
/// <see cref="DemoData.DriverCustId"/>; Real resolves the stored claim. An unavailable
/// selected scope refuses the lookup. Without a selected scope, controlled callers use
/// current feature eligibility for the Demo override.
/// </para>
/// </summary>
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

/// <summary>A resolved Driver identity together with its acquisition namespace.</summary>
public sealed record SubjectDriver(long CustomerId, DataProvenance Provenance);
