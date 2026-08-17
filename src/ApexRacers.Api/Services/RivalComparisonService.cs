using ApexRacers.Api.Dtos;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

/// <summary>
/// Builds the head-to-head comparison between the caller and a rival: each side's identity,
/// licenses, career and iRating history (via <see cref="DriverStatsService"/>), plus the
/// shared-race record assembled from local subsession results.
/// </summary>
public class RivalComparisonService(DriverStatsService stats, AppDbContext db)
{
    public async Task<DriverComparisonDto> CompareAsync(
        long subjectDriverCustId, long rivalCustId, CancellationToken ct = default)
    {
        var you = await stats.GetComparisonSideAsync(subjectDriverCustId, ct);
        var rival = await stats.GetComparisonSideAsync(rivalCustId, ct);
        var shared = await BuildSharedAsync(subjectDriverCustId, rivalCustId, ct);
        return new DriverComparisonDto(you, rival, shared);
    }

    /// <summary>Races both drivers ran, joined from local subsession results + track identity.</summary>
    private async Task<SharedRaceSummaryDto> BuildSharedAsync(
        long subjectDriverCustId, long rivalCustId, CancellationToken ct)
    {
        var subjectDriverResults = db.SubsessionResults
            .Where(r => r.CustId == subjectDriverCustId);
        var rivalResults = db.SubsessionResults.Where(r => r.CustId == rivalCustId);

        var rows = await (
            from subjectDriverResult in subjectDriverResults
            join rivalResult in rivalResults
                on subjectDriverResult.SubsessionId equals rivalResult.SubsessionId
            join sub in db.Subsessions on subjectDriverResult.SubsessionId equals sub.Id
            join trk in db.Tracks on sub.TrackId equals trk.Id
            select new SharedRaceInput(
                subjectDriverResult.SubsessionId,
                sub.StartTime,
                trk.Id,
                trk.Name,
                trk.ConfigName,
                subjectDriverResult.FinishPosition,
                rivalResult.FinishPosition,
                subjectDriverResult.NewIRating - subjectDriverResult.OldIRating,
                rivalResult.NewIRating - rivalResult.OldIRating,
                subjectDriverResult.Incidents,
                rivalResult.Incidents,
                subjectDriverResult.BestLapSeconds,
                rivalResult.BestLapSeconds)).ToListAsync(ct);

        return SharedRaceAnalysis.Summarize(rows);
    }
}
