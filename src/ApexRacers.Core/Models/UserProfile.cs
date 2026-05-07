namespace ApexRacers.Core.Models;

public class UserProfile
{
    public Guid Id { get; set; }
    public long IRacingCustomerId { get; set; }
    public required string DisplayName { get; set; }

    public ICollection<CarPercentileResult> CarPercentileResults { get; set; } = [];
}
