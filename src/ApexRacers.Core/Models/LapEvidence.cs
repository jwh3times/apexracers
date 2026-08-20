using System.Text.Json.Serialization;

namespace ApexRacers.Core.Models;

/// <summary>
/// Which kind of evidence recorded a Lap. A Lap reaches ApexRacers either from iRacing's results
/// or from a User's telemetry, and the two are never interchangeable — a Personal Best drawn from
/// them names the evidence it came from.
/// </summary>
/// <remarks>
/// Serialized by name so the wire contract reads as <c>"RaceLap"</c> / <c>"UploadedLap"</c> rather
/// than as an ordinal a reader would have to decode.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<LapEvidence>))]
public enum LapEvidence : byte
{
    /// <summary>A Lap driven in the racing Sim Session of a Subsession, attributed by iRacing.</summary>
    RaceLap = 1,

    /// <summary>A Lap known only because a User submitted the telemetry that recorded it.</summary>
    UploadedLap = 2,
}
