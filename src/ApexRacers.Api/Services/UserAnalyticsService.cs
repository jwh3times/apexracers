using ApexRacers.Api.Dtos;
using ApexRacers.Core;
using ApexRacers.Data;

namespace ApexRacers.Api.Services;

public class UserAnalyticsService(AppDbContext db, SubjectDriverContext? subjects = null,
    PercentileRefresh? refresh = null)
{
    public async Task<List<CarAnalyticsDto>> GetAnalyticsAsync(Guid userId, int? seriesId,
        PersonalBestEvidence evidence, CancellationToken ct = default)
    {
        if (db.Provenance != DataProvenance.Demo) return [];
        var subject = await (subjects ?? new SubjectDriverContext(db, new FeatureFlagEligibility(db)))
            .GetSubjectDriverAsync(userId, ct);
        if (subject is not { Provenance: DataProvenance.Demo }) return [];
        var snapshot = await (refresh ?? new PercentileRefresh(db, TimeProvider.System,
            new UnavailableDriverEnforcementJournal())).ReadDemoHistoryAsync(userId, evidence.Selection, seriesId, ct);
        return PercentileAnalyticsProjection.Build(snapshot);
    }
}
