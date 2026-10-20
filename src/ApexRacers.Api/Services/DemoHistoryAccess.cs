using ApexRacers.Core;
using ApexRacers.Data;

namespace ApexRacers.Api.Services;

/// <summary>Admits authenticated Demo Subject reads before entering classified history.
/// Null means the Demo path is inapplicable; an admitted empty snapshot or failed refresh
/// never grants a caller permission to fall back to transient Driver evidence.</summary>
internal sealed class DemoHistoryAccess(AppDbContext db, SubjectDriverContext? subjects = null,
    PercentileRefresh? refresh = null)
{
    private SubjectDriverContext Subjects { get; } = subjects ?? new(db, new FeatureFlagEligibility(db));
    private PercentileRefresh Refresh { get; } = refresh ?? new(db, TimeProvider.System,
        new UnavailableDriverEnforcementJournal());

    public async Task<PercentileHistorySnapshot?> ReadAsync(Guid? callerUserId, PersonalBestEvidence evidence,
        int? seriesId, long? requestedCustomerId = null, CancellationToken ct = default)
    {
        if (db.Provenance != DataProvenance.Demo || callerUserId is not { } userId) return null;
        var subject = await Subjects.GetSubjectDriverAsync(userId, ct);
        if (subject is not { Provenance: DataProvenance.Demo }
            || requestedCustomerId is { } customerId && subject.CustomerId != customerId) return null;
        return await Refresh.ReadDemoHistoryAsync(userId, evidence.Selection, seriesId, ct);
    }
}
