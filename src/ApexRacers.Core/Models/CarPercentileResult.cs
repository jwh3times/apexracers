namespace ApexRacers.Core.Models;

public class CarPercentileResult
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public int CarId { get; set; }
    public int WeekId { get; set; }
    public double PercentileRank { get; set; }
    public int SampleSize { get; set; }
    public DateTimeOffset ComputedAt { get; set; }

    public UserProfile User { get; set; } = null!;
    public Car Car { get; set; } = null!;
    public Week Week { get; set; } = null!;
}
