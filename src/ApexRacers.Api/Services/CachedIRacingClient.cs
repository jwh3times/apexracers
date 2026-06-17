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
public class CachedIRacingClient(AppDbContext db, IServiceProvider sp)
{
    /// <summary>True when the iRacing client is registered (credentials configured).</summary>
    public bool IsConfigured => sp.GetService<IDataClient>() is not null;

    /// <summary>
    /// Returns the cached value for <paramref name="cacheKey"/> when present and unexpired,
    /// otherwise invokes <paramref name="fetch"/> against the live client, stores the result
    /// for <paramref name="ttl"/>, and returns it.
    /// </summary>
    public async Task<T> GetOrFetchAsync<T>(
        string cacheKey,
        TimeSpan ttl,
        Func<IDataClient, Task<T>> fetch,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var row = await db.ExternalDataCaches.FirstOrDefaultAsync(c => c.CacheKey == cacheKey, ct);
        if (row is not null && row.ExpiresAt > now)
            return JsonSerializer.Deserialize<T>(row.Payload)!;

        var client = sp.GetService<IDataClient>()
            ?? throw new IRacingNotConfiguredException();

        var fresh = await fetch(client);
        var json = JsonSerializer.Serialize(fresh);

        if (row is null)
        {
            db.ExternalDataCaches.Add(new ExternalDataCache
            {
                CacheKey = cacheKey,
                Payload = json,
                FetchedAt = now,
                ExpiresAt = now + ttl,
            });
        }
        else
        {
            row.Payload = json;
            row.FetchedAt = now;
            row.ExpiresAt = now + ttl;
        }

        await db.SaveChangesAsync(ct);
        return fresh;
    }
}

/// <summary>Thrown when an iRacing fetch is attempted but no credentials are configured.</summary>
public sealed class IRacingNotConfiguredException()
    : Exception("iRacing integration is not configured on this server.");
