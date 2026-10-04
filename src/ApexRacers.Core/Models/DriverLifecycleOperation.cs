namespace ApexRacers.Core.Models;

public class DriverLifecycleOperation
{
    public Guid Id { get; set; }
    public Guid GrantId { get; set; }
    public DriverLifecycleKind Kind { get; set; }
    public DateTimeOffset OriginalLossAt { get; set; }
    public long AppliedRevision { get; set; }
    public DateTimeOffset PrimaryAppliedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
