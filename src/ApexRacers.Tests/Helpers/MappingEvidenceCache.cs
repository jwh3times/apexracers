using System.Text.Json;
using ApexRacers.Api.Services;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using Aydsko.iRacingData;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Tests.Helpers;

/// <summary>Unit mapping harness for existing SDK-to-owned-response tests. Its provider is a
/// test double and its rows are stored-evidence fixtures, not runtime acquisition or consent.
/// Acquisition/fencing tests deliberately use the ordinary CachedIRacingClient and migrated PostgreSQL.</summary>
internal sealed class MappingEvidenceCache : CachedIRacingClient
{
    private readonly AppDbContext db;
    private readonly IDataClient? provider;
    public MappingEvidenceCache(AppDbContext context, IDataClient? client) : base(context, client)
    { db = context; provider = client; }
    public override async Task<T> GetOrFetchAsync<T>(CacheSpec spec, Func<IDataClient, Task<T>> fetch, CancellationToken ct)
    {
        MappedEvidenceContract.RequireOwned<T>();
        var row = await db.ExternalDataCaches.FirstOrDefaultAsync(c => c.CacheKey == spec.Key, ct);
        if (row is not null && row.ExpiresAt > DateTimeOffset.UtcNow) return JsonSerializer.Deserialize<T>(row.Payload)!;
        if (provider is null) throw new IRacingNotConfiguredException();
        var mapped = MapEvidence(await fetch(provider));
        if (row is null)
        {
            row = new ExternalDataCache { CacheKey = spec.Key, Payload = "", FetchedAt = DateTimeOffset.UtcNow };
            db.ExternalDataCaches.Add(row);
        }
        row.Payload = JsonSerializer.Serialize(mapped);
        row.ExpiresAt = row.FetchedAt + spec.Ttl;
        await db.SaveChangesAsync(ct);
        return mapped;
    }
}
