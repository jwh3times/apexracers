namespace ApexRacers.Core.Models;

/// <summary>Aggregate cutover inventory; RecordedAt is an observation time, never an authorization-loss time.</summary>
public class ProvenanceMigrationInventory
{
    public required string StorageKind { get; set; }
    public long UnknownRows { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}
