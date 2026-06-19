using ApexRacers.Api.Dtos;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

/// <summary>
/// Builds the head-to-head comparison between the caller and a rival: each side's identity,
/// licenses, career and iRating history (via <see cref="MemberStatsService"/>), plus the
/// shared-race record assembled from local subsession results.
/// </summary>
public class RivalComparisonService(MemberStatsService stats, AppDbContext db)
{
    public async Task<DriverComparisonDto> CompareAsync(
        long callerCustId, long rivalCustId, CancellationToken ct)
    {
        var you = await stats.GetComparisonSideAsync(callerCustId, ct);
        var rival = await stats.GetComparisonSideAsync(rivalCustId, ct);
        var shared = await BuildSharedAsync(callerCustId, rivalCustId, ct);
        return new DriverComparisonDto(you, rival, shared);
    }

    /// <summary>Races both drivers ran, joined from local subsession results + track names.</summary>
    private async Task<SharedRaceSummaryDto> BuildSharedAsync(long me, long rival, CancellationToken ct)
    {
        var mine = db.SubsessionResults.Where(r => r.CustId == me);
        var theirs = db.SubsessionResults.Where(r => r.CustId == rival);

        var rows = await (
            from m in mine
            join t in theirs on m.SubsessionId equals t.SubsessionId
            join sub in db.Subsessions on m.SubsessionId equals sub.Id
            join trk in db.Tracks on sub.TrackId equals trk.Id
            select new SharedRaceInput(
                m.SubsessionId,
                sub.StartTime,
                trk.Name,
                m.FinishPosition,
                t.FinishPosition,
                m.NewIRating - m.OldIRating,
                t.NewIRating - t.OldIRating,
                m.Incidents,
                t.Incidents,
                m.BestLapSeconds,
                t.BestLapSeconds)).ToListAsync(ct);

        return SharedRaceAnalysis.Summarize(rows);
    }
}
