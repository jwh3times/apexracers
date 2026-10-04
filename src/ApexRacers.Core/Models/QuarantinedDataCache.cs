namespace ApexRacers.Core.Models;

/// <summary>Pre-cutover mapped payloads with unestablished origin; never a live cache fallback.</summary>
public class QuarantinedDataCache
{
    public int Id { get; set; }
    public required string CacheKey { get; set; }
    public required string Payload { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
