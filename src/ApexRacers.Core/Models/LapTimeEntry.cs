namespace ApexRacers.Core.Models;

public class LapTimeEntry
{
    public long Id { get; set; }
    public long DriverCustomerId { get; set; }
    public int CarId { get; set; }
    public int WeekId { get; set; }
    public double LapTimeSeconds { get; set; }
    public DateTimeOffset RecordedAt { get; set; }

    public Car Car { get; set; } = null!;
    public Week Week { get; set; } = null!;
}
