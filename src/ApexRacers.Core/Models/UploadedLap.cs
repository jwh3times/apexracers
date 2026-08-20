namespace ApexRacers.Core.Models;

/// <summary>
/// One Uploaded Lap — a timed Lap ApexRacers knows only because a User submitted the telemetry
/// that recorded it. Owned by that User and attributed to the Driver the file named.
/// </summary>
/// <remarks>
/// Named for the evidence that produced it, not for what it might turn out to be. "Personal Lap"
/// read as "a lap that is a personal best", which is not what these rows are: a Personal Best is
/// chosen from a Race Best and an Uploaded Best together, and most of these rows are neither.
/// </remarks>
public class UploadedLap
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
