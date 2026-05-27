namespace ApexRacers.Core.Models;

public class PersonalLap
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public int CarId { get; set; }
    public int IracingTrackId { get; set; }
    public required string TrackName { get; set; }
    public required string ConfigName { get; set; }
    public double LapTimeSeconds { get; set; }
    public bool IsValidLap { get; set; }
    public float AirTempCelsius { get; set; }
    public float TrackTempCelsius { get; set; }
    public byte TrackWetness { get; set; }
    public DateTimeOffset RecordedAt { get; set; }

    public Car Car { get; set; } = null!;
}
