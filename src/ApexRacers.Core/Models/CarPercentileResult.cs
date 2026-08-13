namespace ApexRacers.Core.Models;

public class CarPercentileResult
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public int CarId { get; set; }
    public int SeriesId { get; set; }
    public Guid WeekId { get; set; }
    public double PercentileRank { get; set; }

    /// <summary>
    /// The driver's placement as a whole-number share of the Field. Stored rather than derived,
    /// because a percentile rank cannot be inverted back to a placement once computed.
    /// </summary>
    public int TopSharePercent { get; set; }

    /// <summary>The size of the Field, counting the driver themselves.</summary>
    public int SampleSize { get; set; }
    public DateTimeOffset ComputedAt { get; set; }

    public Car Car { get; set; } = null!;
    public Week Week { get; set; } = null!;
}
