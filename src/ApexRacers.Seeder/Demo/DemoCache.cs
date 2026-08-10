using System.Text.Json;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Seeder.Demo;

/// <summary>
/// Writes synthetic demo rows into <c>ExternalDataCaches</c> so the CachedIRacingClient-backed
/// endpoints serve them as hits while real iRacing creds are absent. Payloads are serialized with
/// System.Text.Json default options — identical to <c>CachedIRacingClient.GetOrFetchAsync</c> — so
/// the real services deserialize them as their own cached <c>T</c>.
/// </summary>
public static class DemoCache
{
    /// <summary>
    /// Lower bound that identifies every synthetic demo cache row. The production teardown mirror
    /// is <c>src/ApexRacers.Data/Seeds/purge_demo_data.sql</c>; its value and <c>&gt;=</c> operator
    /// must remain in lockstep with this owner.
    /// </summary>
    public static readonly DateTimeOffset SentinelThreshold =
        new(9000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Far-future expiry written to demo cache rows; always inside the sentinel range.</summary>
    public static readonly DateTimeOffset Sentinel = new(9999, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Fixed reference date for deterministic payload dates (keeps builders unit-testable).</summary>
    public static readonly DateTimeOffset RefDate = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    public static async Task UpsertAsync<T>(AppDbContext db, string key, T value, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(value);
        var row = await db.ExternalDataCaches.FirstOrDefaultAsync(c => c.CacheKey == key, ct);
        if (row is null)
        {
            db.ExternalDataCaches.Add(new ExternalDataCache
            {
                CacheKey = key,
                Payload = json,
                FetchedAt = DateTimeOffset.UtcNow,
                ExpiresAt = Sentinel,
            });
        }
        else
        {
            row.Payload = json;
            row.FetchedAt = DateTimeOffset.UtcNow;
            row.ExpiresAt = Sentinel;
        }
        await db.SaveChangesAsync(ct);
    }
}
