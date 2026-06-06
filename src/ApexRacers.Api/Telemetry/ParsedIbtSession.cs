using ApexRacers.Core.Models;

namespace ApexRacers.Api.Telemetry;

public sealed class ParsedIbtSession
{
    public required int IracingTrackId { get; init; }
    public required string TrackName { get; init; }
    public required string ConfigName { get; init; }
    public required int IracingCarId { get; init; }
    public required string CarName { get; init; }
    public required string CarNameAbbreviated { get; init; }
    public required long DriverCustomerId { get; init; }
    public required string DriverName { get; init; }
    public float AirTempCelsius { get; init; }
    public float TrackTempCelsius { get; init; }
    public byte TrackWetness { get; init; }
    public LapSessionType SessionType { get; init; }
    public DateTimeOffset SessionDate { get; init; }
    public required IReadOnlyList<ParsedLap> Laps { get; init; }
}

public sealed class ParsedLap
{
    public int LapNumber { get; init; }
    public double LapTimeSeconds { get; init; }
    public bool IsValid { get; init; }
}
