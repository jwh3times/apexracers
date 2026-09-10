namespace ApexRacers.Core.Models;

/// <summary>
/// A single cached external (iRacing) API response. Keyed by an opaque
/// <see cref="CacheKey"/> (e.g. "chart:691062:2:1") with the serialized typed
/// result stored whole in <see cref="Payload"/>. Backs <c>CachedIRacingClient</c>'s
/// get-or-fetch so repeated reads come from Postgres and we stay within rate limits.
/// </summary>
public class ExternalDataCache
{
    /// <summary>
    /// Storage limit on <see cref="CacheKey"/>, shared by the EF configuration that enforces it
    /// and by the cache client that refuses to build a key it could never persist. A key over this
    /// length does not merely fail to cache — the insert throws, and the get-or-fetch path treats a
    /// failed insert as a lost cold-start race, so every subsequent request for that key would go
    /// live to iRacing forever (GHSA-jv96-89xc-98h2).
    /// </summary>
    public const int CacheKeyMaxLength = 200;

    public int Id { get; set; }
    public required string CacheKey { get; set; }
    public required string Payload { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
