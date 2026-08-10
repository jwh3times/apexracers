using ApexRacers.Core.Models;
using Aydsko.iRacingData.Common;

namespace ApexRacers.Ingestion;

/// <summary>
/// Maps the SDK track-state block to the app-owned persisted contract.
/// </summary>
public static class TrackStateIngest
{
    public static TrackStateSnapshot? ToSnapshot(TrackState? source) =>
        source is null
            ? null
            : new TrackStateSnapshot
            {
                LeaveMarbles = source.LeaveMarbles,
                PracticeRubber = source.PracticeRubber,
                QualifyRubber = source.QualifyRubber,
                WarmupRubber = source.WarmupRubber,
                RaceRubber = source.RaceRubber,
                PracticeGripCompound = source.PracticeGripCompound,
                QualifyGripCompound = source.QualifyGripCompound,
                WarmupGripCompound = source.WarmupGripCompound,
                RaceGripCompound = source.RaceGripCompound,
            };
}
