using ApexRacers.Core.PercentileHistory;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services.PercentileHistory;

/// <summary>A resolution-time snapshot only. Later preparation, commit and protected dispatch must
/// re-resolve current authority and compare generations; retaining this object grants no access.</summary>
public sealed class PercentileHistoryOwner
{
    internal PercentileHistoryOwner(PercentileOwnerIdentity identity, Guid? grantId, long? revision)
    { Identity = identity; GrantId = grantId; Revision = revision; }
    public PercentileOwnerIdentity Identity { get; }
    public Guid? GrantId { get; }
    public long? Revision { get; }
    public bool IsSubjectDriver(long customerId) => Identity.CustomerId == customerId;
}

/// <summary>Separate credential-free Demo policy from verified Personal authority. Callers supply
/// their authenticated User only; arbitrary Driver lookup never selects a history owner.</summary>
public sealed class PercentileHistoryOwnerResolver(
    AppDbContext db, SubjectDriverContext subjects, DriverAuthorization authority)
{
    public async Task<PercentileHistoryOwner?> ResolveDemoAsync(Guid authenticatedUserId, CancellationToken ct = default)
    {
        if (db.Provenance != DataProvenance.Demo) return null;
        var subject = await subjects.GetSubjectDriverAsync(authenticatedUserId, ct);
        return subject is { Provenance: DataProvenance.Demo }
            ? new(new(authenticatedUserId, subject.CustomerId, subject.Provenance, PercentileOwnerKind.DemoSubject), null, null)
            : null;
    }

    public async Task<PercentileHistoryOwner?> ResolvePersonalAsync(Guid authenticatedUserId, CancellationToken ct = default)
    {
        if (db.Provenance == DataProvenance.Unknown) return null;
        var bindings = await db.Set<DriverAuthorizationGrant>().AsNoTracking()
            .Where(g => g.UserId == authenticatedUserId && g.Provenance == db.Provenance && g.BindingActive)
            .Select(g => new { g.CustomerId, g.Id, g.Revision }).Take(2).ToListAsync(ct);
        if (bindings.Count > 1) throw new InvalidOperationException("Conflicting current Driver associations.");
        if (bindings.Count == 0) return null;
        var binding = bindings[0];
        var access = await authority.ResolveAsync(new(authenticatedUserId, binding.CustomerId, db.Provenance), DriverConsentScope.Personal, ct);
        if (access is null || access.GrantId != binding.Id || access.Revision != binding.Revision) return null;
        return new(new(authenticatedUserId, access.Scope.CustomerId, access.Scope.Provenance, PercentileOwnerKind.Personal),
            access.GrantId, access.Revision);
    }
}
