using ApexRacers.Core.PercentileHistory;

namespace ApexRacers.Core.Models;

/// <summary>A classified current reading. Identity data is never proof of current authority.</summary>
public sealed class ClassifiedCarPercentileResult : IManagedEvidence
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public int CustomerId { get; set; }
    public DataProvenance Provenance { get; set; }
    public PercentileOwnerKind OwnerKind { get; set; }
    public Guid? GrantId { get; set; }
    public long? GrantRevision { get; set; }
    public int SelectionPolicyVersion { get; set; }
    public string SelectionKey { get; set; } = null!;
    public int CarId { get; set; }
    public int SeriesId { get; set; }
    public Guid WeekId { get; set; }
    public double PercentileRank { get; set; }
    public int TopSharePercent { get; set; }
    public int SampleSize { get; set; }
    public DateTimeOffset ComputedAt { get; set; }
    public double PersonalBestLapSeconds { get; set; }
    public LapEvidence PersonalBestLapEvidence { get; set; }
    public LapSessionType? UploadedSessionType { get; set; }
    public long ReadingRevision { get; set; }
    public Guid? EvidenceCopyId { get; set; }
    public Guid PreparationPurposeId { get; set; }
    public long PreparationGeneration { get; set; }
    public long PreparationVersion { get; set; }
}
