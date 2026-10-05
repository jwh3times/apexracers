namespace ApexRacers.Core.Models;

/// <summary>Collection and continuing retention are separate. An inactive Season does not end a purpose.</summary>
public class EvidencePurpose
{
    public Guid Id { get; set; }
    public DataProvenance Provenance { get; set; }
    public EvidencePurposeKind Kind { get; set; }
    public int? SeasonId { get; set; }
    public Guid? GrantId { get; set; }
    public long? GrantRevision { get; set; }
    public Guid? PublicationAdmissionId { get; set; }
    public Guid? HistoricalRequestId { get; set; }
    public DateTimeOffset? HistoricalRequestEndedAt { get; set; }
    public long Generation { get; set; } = 1;
    public long EvidenceVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? OriginalEndedAt { get; set; }
}
