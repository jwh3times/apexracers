using ApexRacers.Core.Models;

namespace ApexRacers.Core;

/// <summary>
/// The fastest Lap known for a Subject Driver in one Car at one Track, together with the evidence
/// that produced it. This is the lap ranked against a Field.
/// </summary>
/// <remarks>
/// The choice between a Race Best and an Uploaded Best and the record of which one won are the
/// same decision, so they are made in one place. Three call sites previously each ran their own
/// comparison and kept only the number, which is why no response could say where the lap came
/// from — and why the Field a driver is ranked against, composed entirely of Race Laps, could be
/// silently compared with a lap that was never raced.
/// </remarks>
public readonly record struct PersonalBest(double LapSeconds, LapEvidence Evidence)
{
    /// <summary>
    /// Chooses the Personal Best from the two evidence-side bests, either of which may be absent.
    /// Returns <c>null</c> when the Driver has neither.
    /// </summary>
    /// <remarks>
    /// A Race Best wins an exact tie: it was set against the Field being ranked, so it is the
    /// better-evidenced of two equally fast laps. This preserves the strict "faster than" test the
    /// three original call sites all happened to share.
    /// </remarks>
    public static PersonalBest? Select(double? raceBest, double? uploadedBest) => (raceBest, uploadedBest) switch
    {
        (null, null)                        => null,
        (null, { } uploaded)                => new PersonalBest(uploaded, LapEvidence.UploadedLap),
        ({ } race, null)                    => new PersonalBest(race, LapEvidence.RaceLap),
        ({ } race, { } uploaded)            => uploaded < race
            ? new PersonalBest(uploaded, LapEvidence.UploadedLap)
            : new PersonalBest(race, LapEvidence.RaceLap),
    };

    /// <summary>
    /// Chooses the Personal Best for a Driver already known to hold a Race Best, so the result is
    /// never absent. Same comparison as the nullable overload — kept as an overload so a caller
    /// that has proved the Race Best exists does not have to suppress a null it cannot get.
    /// </summary>
    public static PersonalBest Select(double raceBest, double? uploadedBest) =>
        Select((double?)raceBest, uploadedBest)!.Value;
}
