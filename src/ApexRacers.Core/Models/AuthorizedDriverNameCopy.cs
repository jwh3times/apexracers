namespace ApexRacers.Core.Models;

/// <summary>Names are separately scoped and never snapshotted into shared official evidence.</summary>
public class AuthorizedDriverNameCopy : IManagedEvidence
{
    public Guid Id { get; set; }
    public DataProvenance Provenance { get; set; }
    public Guid? EvidenceCopyId { get; set; }
    public Guid GrantId { get; set; }
    public required string DriverName { get; set; }
}
