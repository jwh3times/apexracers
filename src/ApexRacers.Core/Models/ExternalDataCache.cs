namespace ApexRacers.Core.Models;

/// <summary>
/// A single cached external (iRacing) API response. Keyed by an opaque
/// <see cref="CacheKey"/> (e.g. "chart:691062:2:1") with the serialized typed
/// result stored whole in <see cref="Payload"/>. Backs <c>CachedIRacingClient</c>'s
/// get-or-fetch so repeated reads come from Postgres and we stay within rate limits.
/// </summary>
public class ExternalDataCache
{
    public int Id { get; set; }
    public required string CacheKey { get; set; }
    public required string Payload { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
