namespace ApexRacers.Core.Models;

/// <summary>
/// A driver the user follows for head-to-head comparison. One row per (user, rival cust_id);
/// the display name is snapshotted at add time for listing without an extra fetch.
/// </summary>
public class Rival
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public long RivalCustId { get; set; }
    public required string DisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
