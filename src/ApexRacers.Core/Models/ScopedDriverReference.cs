namespace ApexRacers.Core.Models;

/// <summary>Restricted association metadata; raw references and Driver names are never stored.</summary>
public class ScopedDriverReference
{
    public string TokenHash { get; set; } = string.Empty;
    public Guid RecipientGrantId { get; set; }
    public long RecipientRevision { get; set; }
    public Guid RecipientProofId { get; set; }
    public Guid TargetGrantId { get; set; }
    public long TargetRevision { get; set; }
    public Guid TargetProofId { get; set; }
    public DriverReferencePurpose Purpose { get; set; }
    public DataProvenance Provenance { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>Private internal association, never a public Driver key or a name snapshot.</summary>
public class PrivateDriverFollow
{
    public Guid Id { get; set; }
    public Guid RecipientGrantId { get; set; }
    public Guid TargetGrantId { get; set; }
    public DataProvenance Provenance { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool Active { get; set; } = true;
    public DateTimeOffset? OriginalLossAt { get; set; }
    public DateTimeOffset? ReactivateBefore { get; set; }
    public DateTimeOffset? RemoveBy { get; set; }
}
