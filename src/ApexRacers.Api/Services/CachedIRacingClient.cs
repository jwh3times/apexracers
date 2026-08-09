using System.Text.Json;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using Aydsko.iRacingData;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

/// <summary>
/// Get-or-fetch cache in front of the iRacing Data API. Every on-demand and bulk
/// external fetch goes through here so repeated reads are served from Postgres and
/// we stay within iRacing's rate limits. The underlying <see cref="IDataClient"/> is
/// registered only when iRacing credentials are present (see Program.cs); when it is
/// not, a cache miss throws <see cref="IRacingNotConfiguredException"/> (callers map
/// that to a 503).
/// </summary>
/// <remarks>
/// <paramref name="client"/> is nullable rather than resolved from an <c>IServiceProvider</c>:
/// nullability already expresses "credentials absent", which is the only reason the dependency
/// was service-located in the first place. Taking it directly puts the seam on
/// <see cref="IDataClient"/> — where a substitute naturally goes — instead of on the container,
/// which had forced twelve test files to each declare an identical stub returning the same object
/// for any requested type.
/// </remarks>
public class CachedIRacingClient(AppDbContext db, IDataClient? client)
{
    /// <summary>
    /// Returns the cached value for <paramref name="spec"/> when present and unexpired, otherwise
    /// invokes <paramref name="fetch"/> against the live client, stores the result for the spec's
    /// TTL, and returns it.
    /// </summary>
    public async Task<T> GetOrFetchAsync<T>(
        CacheSpec spec,
        Func<IDataClient, Task<T>> fetch,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var row = await db.ExternalDataCaches.FirstOrDefaultAsync(c => c.CacheKey == spec.Key, ct);
        if (row is not null && row.ExpiresAt > now)
            return JsonSerializer.Deserialize<T>(row.Payload)!;

        var live = client ?? throw new IRacingNotConfiguredException();

        var fresh = await fetch(live);
        var json = JsonSerializer.Serialize(fresh);

        if (row is null)
        {
            db.ExternalDataCaches.Add(new ExternalDataCache
            {
                CacheKey = spec.Key,
                Payload = json,
                FetchedAt = now,
                ExpiresAt = now + spec.Ttl,
            });
        }
        else
        {
            row.Payload = json;
            row.FetchedAt = now;
            row.ExpiresAt = now + spec.Ttl;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (row is null)
        {
            // Cold-start race: two concurrent misses on a key with no row both reach the insert,
            // and CacheKey is unique, so the loser takes a 23505. Nothing maps DbUpdateException,
            // so it surfaced as a 500 — most exposed on /live, whose single global "race-guide"
            // key is public and uncached on a cold or freshly-purged cache.
            //
            // The winner already wrote the value we were about to write, so detach our losing
            // insert and hand back the fetched value. An expiry is an *update*, not an insert,
            // which is why this only needs the row-is-null case.
            db.ChangeTracker.Clear();
        }

        return fresh;
    }
}

/// <summary>Thrown when an iRacing fetch is attempted but no credentials are configured.</summary>
public sealed class IRacingNotConfiguredException()
    : Exception("iRacing integration is not configured on this server.");
