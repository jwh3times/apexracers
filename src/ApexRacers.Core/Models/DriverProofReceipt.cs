namespace ApexRacers.Core.Models;

public class DriverProofReceipt
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public int CustomerId { get; set; }
    public DataProvenance Provenance { get; set; }
    public DateTimeOffset VerifiedAt { get; set; }
    public required string Authority { get; set; }
}
