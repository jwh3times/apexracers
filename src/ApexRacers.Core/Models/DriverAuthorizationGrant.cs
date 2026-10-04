namespace ApexRacers.Core.Models;

public class DriverAuthorizationGrant
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public int CustomerId { get; set; }
    public DataProvenance Provenance { get; set; }
    public long Revision { get; set; } = 1;
    public bool BindingActive { get; set; } = true;
    public Guid ProofReceiptId { get; set; }
    public bool ProofValid { get; set; }
    public string? PersonalConsentVersion { get; set; }
    public string? SharingConsentVersion { get; set; }
    public string? AuthorizedDriverName { get; set; }
    public DateTimeOffset? PersonalClosedAt { get; set; }
    public DateTimeOffset? SharingClosedAt { get; set; }
}
