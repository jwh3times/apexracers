namespace ApexRacers.Core.Models;

/// <summary>Minimal durable copy fence; never contains a name, profile, token or payload.</summary>
public class EvidenceCopyMarker
{
    public Guid Id { get; set; }
    public Guid PurposeId { get; set; }
    public long Generation { get; set; }
    public DataProvenance Provenance { get; set; }
    public EvidenceCopyKind Kind { get; set; }
    public required string KeyHash { get; set; }
    public long Version { get; set; }
    public DateTimeOffset OriginalAcquiredAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? UnavailableAt { get; set; }
    public DateTimeOffset? RemovalDueAt { get; set; }
    public DateTimeOffset? VerifiedRemovedAt { get; set; }
}
