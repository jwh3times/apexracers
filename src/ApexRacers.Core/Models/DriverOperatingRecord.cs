namespace ApexRacers.Core.Models;

/// <summary>Controlled synthetic state only. Never loaded as production operating authority.</summary>
public sealed class DriverOperatingRecord
{
    public int Id { get; set; } = 1;
    public string Snapshot { get; set; } = "";
    public DateTimeOffset WindowStartedAt { get; set; }
    public int AcquisitionUsed { get; set; }
    public int PublicationUsed { get; set; }
    public string Reservations { get; set; } = "[]";
}
