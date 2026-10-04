namespace ApexRacers.Core.Models;

public class DriverPublicationAdmission
{
    public Guid Id { get; set; }
    public Guid GrantId { get; set; }
    public long Revision { get; set; }
    public DriverConsentScope Purpose { get; set; }
    public Guid Incarnation { get; set; }
    public DateTimeOffset AdmittedAt { get; set; }
    public DateTimeOffset LeaseUntil { get; set; }
    public DateTimeOffset? TerminalAt { get; set; }
}
