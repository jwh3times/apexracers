namespace ApexRacers.Core.Models;

public class DriverCopyCleanup
{
    public Guid Id { get; set; }
    public Guid OperationId { get; set; }
    public Guid GrantId { get; set; }
    public DriverConsentScope Purpose { get; set; }
    public long ThroughRevision { get; set; } = 1;
    public DateTimeOffset OriginalLossAt { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public DateTimeOffset? VerifiedRemovedAt { get; set; }
}
