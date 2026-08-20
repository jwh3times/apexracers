using ApexRacers.Core.Models;

namespace ApexRacers.Api.Services;

/// <summary>
/// Owns which lap evidence may contribute to a Subject Driver's Personal Best.
/// </summary>
public sealed class PersonalBestEvidence(
    bool includesUploadedLaps,
    IReadOnlyList<LapSessionType>? requestedUploadedLapTypes)
{
    private readonly LapSessionType[] uploadedLapTypes = requestedUploadedLapTypes?
        .Distinct()
        .ToArray() ?? [];

    public static PersonalBestEvidence OfficialRaceLapsOnly { get; } = new(false, null);

    public bool IncludesUploadedLaps { get; } = includesUploadedLaps;

    public static PersonalBestEvidence FromRequest(
        bool includeUploadedLaps,
        IReadOnlyList<LapSessionType>? uploadedLapTypes)
    {
        if (!includeUploadedLaps)
            return OfficialRaceLapsOnly;

        return new PersonalBestEvidence(true, uploadedLapTypes);
    }

    public IQueryable<UploadedLap> ScopeUploadedLaps(IQueryable<UploadedLap> laps)
    {
        if (!IncludesUploadedLaps)
            return laps.Where(_ => false);

        if (uploadedLapTypes.Length == 0)
            return laps;

        var types = uploadedLapTypes;
        return laps.Where(p => types.Contains(p.SessionType) || p.SessionType == LapSessionType.Unknown);
    }
}
