using System.Text.Json;
using ApexRacers.Core;
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
    /// <summary>Far-future freshness expiry; provenance separately identifies Demo copies.</summary>
    public static DateTimeOffset Sentinel => DemoData.CacheSentinel;

    /// <summary>Fixed reference date for deterministic payload dates (keeps builders unit-testable).</summary>
    public static readonly DateTimeOffset RefDate = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    public static async Task UpsertAsync<T>(AppDbContext db, string key, T value, CancellationToken ct)
    {
        var writer = await SyntheticEvidenceWriter.OpenAsync(db, ct);
        MappedEvidenceContract.RequireOwned<T>();
        var json = JsonSerializer.Serialize(value);
        var row = await db.ExternalDataCaches.FirstOrDefaultAsync(
            c => c.Provenance == DataProvenance.Demo && c.CacheKey == key, ct);
        if (row is null)
        {
            db.ExternalDataCaches.Add(new ExternalDataCache
            {
                CacheKey = key,
                Provenance = DataProvenance.Demo,
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
        await writer.SaveChangesAsync(ct);
    }
}
