namespace ApexRacers.Core.Models;

public class PersonalLap
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>
    /// The Customer ID the telemetry file named as its recording Driver, or null when the file
    /// named none. Rows written before this column existed are also null: the value was never
    /// captured and cannot be recovered, so null means "not established" rather than "no driver".
    /// </summary>
    public long? DriverCustId { get; set; }

    public int CarId { get; set; }
    public int TrackId { get; set; }
    public double LapTimeSeconds { get; set; }
    public bool IsValidLap { get; set; }
    public LapSessionType SessionType { get; set; } = LapSessionType.Unknown;
    public float AirTempCelsius { get; set; }
    public float TrackTempCelsius { get; set; }
    public byte TrackWetness { get; set; }
    public DateTimeOffset RecordedAt { get; set; }

    public Car Car { get; set; } = null!;
    public Track Track { get; set; } = null!;
}
