using System.Text.Json.Serialization;

namespace ApexRacers.Core.Models;

/// <summary>
/// Owned snapshot of the iRacing track-state block persisted in
/// <see cref="Subsession.TrackStateJson"/>. The JSON names match the SDK wire contract already
/// stored in existing rows; do not rename them without a data migration.
/// </summary>
public sealed record TrackStateSnapshot
{
    [JsonPropertyName("leave_marbles")]
    public bool LeaveMarbles { get; init; }

    [JsonPropertyName("practice_rubber")]
    public int PracticeRubber { get; init; }

    [JsonPropertyName("qualify_rubber")]
    public int QualifyRubber { get; init; }

    [JsonPropertyName("warmup_rubber")]
    public int WarmupRubber { get; init; }

    [JsonPropertyName("race_rubber")]
    public int RaceRubber { get; init; }

    [JsonPropertyName("practice_grip_compound")]
    public int PracticeGripCompound { get; init; }

    [JsonPropertyName("qualify_grip_compound")]
    public int QualifyGripCompound { get; init; }

    [JsonPropertyName("warmup_grip_compound")]
    public int WarmupGripCompound { get; init; }

    [JsonPropertyName("race_grip_compound")]
    public int RaceGripCompound { get; init; }
}
