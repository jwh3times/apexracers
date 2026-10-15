using ApexRacers.Core.Models;

namespace ApexRacers.Core.PercentileHistory;

/// <summary>Pure classified payload checks. Neither identifiers nor a valid key establish authority;
/// the copy module rechecks current purpose, grant, sources and preparation under coordination.</summary>
public static class ClassifiedPercentileContract
{
    public static string Key(ClassifiedCarPercentileResult copy)
    {
        var selection = PercentileSelection.FromStorage(copy.SelectionPolicyVersion, copy.SelectionKey);
        if (copy.OwnerKind == PercentileOwnerKind.DemoSubject
            && (copy.GrantRevision is not null || copy.PersonalBestLapEvidence != LapEvidence.RaceLap)
            || copy.OwnerKind == PercentileOwnerKind.Personal && copy.GrantRevision is not > 0
            || !double.IsFinite(copy.PersonalBestLapSeconds) || copy.PersonalBestLapSeconds <= 0
            || !double.IsFinite(copy.PercentileRank) || copy.PercentileRank is <= 0 or >= 100
            || copy.SampleSize <= 0 || copy.TopSharePercent is <= 0 or > 100
            || (copy.PersonalBestLapEvidence switch
            {
                LapEvidence.RaceLap => copy.UploadedSessionType is not null,
                LapEvidence.UploadedLap => copy.UploadedSessionType is not { } type || !selection.Includes(type),
                _ => true,
            }))
            throw new ArgumentException("The classified percentile payload is inconsistent.");
        return PercentileHistoryKey.Create(new(copy.UserId, copy.CustomerId, copy.Provenance, copy.OwnerKind),
            copy.GrantId, selection, copy.CarId, copy.SeriesId, copy.WeekId);
    }
}
