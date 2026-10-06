namespace ApexRacers.Core.Models;

/// <summary>Typed personal source, bound by its marker to the original verified association.
/// No raw file, recorder name or recording YAML is retained.</summary>
public sealed class PrivateUploadSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public int CustomerId { get; set; }
    public DataProvenance Provenance { get; set; }
    public Guid EvidenceCopyId { get; set; }
    public int CarId { get; set; }
    public int TrackId { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public LapSessionType SessionType { get; set; }
    public List<PrivateUploadedLap> Laps { get; set; } = [];
}

public sealed class PrivateUploadedLap
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public int LapNumber { get; set; }
    public double LapTimeSeconds { get; set; }
}
