namespace ApexRacers.Core.Models;

public class DriverTrackedCopy
{
    public Guid Id { get; set; }
    public Guid GrantId { get; set; }
    public long Revision { get; set; }
    public DriverConsentScope Purpose { get; set; }
    public required string Payload { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UnavailableAt { get; set; }
}
