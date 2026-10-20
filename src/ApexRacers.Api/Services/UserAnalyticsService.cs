using ApexRacers.Api.Dtos;
using ApexRacers.Data;

namespace ApexRacers.Api.Services;

public class UserAnalyticsService(AppDbContext db, SubjectDriverContext? subjects = null,
    PercentileRefresh? refresh = null)
{
    private DemoHistoryAccess History { get; } = new(db, subjects, refresh);

    public async Task<List<CarAnalyticsDto>> GetAnalyticsAsync(Guid userId, int? seriesId,
        PersonalBestEvidence evidence, CancellationToken ct = default)
    {
        var snapshot = await History.ReadAsync(userId, evidence, seriesId, ct: ct);
        if (snapshot is null) return [];
        return PercentileAnalyticsProjection.Build(snapshot);
    }
}
