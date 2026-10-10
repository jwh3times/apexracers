using System.Text.Json;
using ApexRacers.Core;
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
/// that to a 503). Credentials also require an issued evidence purpose; the default issuer is closed.
/// </summary>
/// <remarks>
/// <paramref name="client"/> is nullable rather than resolved from an <c>IServiceProvider</c>:
/// nullability already expresses "credentials absent", which is the only reason the dependency
/// was service-located in the first place. Taking it directly puts the seam on
/// <see cref="IDataClient"/> — where a substitute naturally goes — instead of on the container,
/// which had forced twelve test files to each declare an identical stub returning the same object
/// for any requested type.
/// </remarks>
public class CachedIRacingClient(AppDbContext db, IDataClient? client, DataProvenance? source = null,
    IEvidencePurposeIssuer? purposeIssuer = null, IDriverOperatingControls? operating = null,
    IHttpContextAccessor? httpContext = null)
{
    protected static T MapEvidence<T>(T value) => NameFreeIRacingEvidence.Map(value);
    /// <summary>
    /// Returns unexpired eligible owned evidence in the selected namespace. A Real miss requires
    /// Demo teardown and an issued purpose before capturing a receipt and invoking
    /// <paramref name="fetch"/>. Fresh evidence is returned only after an accepted copy commit;
    /// a Demo miss remains unavailable without invoking the provider.
    /// </summary>
    public virtual async Task<T> GetOrFetchAsync<T>(
        CacheSpec spec,
        Func<IDataClient, Task<T>> fetch,
        CancellationToken ct)
    {
        var provenance = source ?? db.Provenance;
        MappedEvidenceContract.RequireOwned<T>();
        // Reject unstorable keys before acquisition. Historically a broad cold-start race catch
        // swallowed insert failures and allowed repeated provider fetches (GHSA-jv96-89xc-98h2).
        // Key factories bound their inputs; this is the backstop for new factories.
        if (spec.Key.Length > ExternalDataCache.CacheKeyMaxLength)
            throw new ArgumentException(
                $"Cache key exceeds the {ExternalDataCache.CacheKeyMaxLength}-character limit " +
                $"({spec.Key.Length}); the key factory must bound its inputs.",
                nameof(spec));

        if (provenance is not (DataProvenance.Real or DataProvenance.Demo))
            throw new IRacingNotConfiguredException();
        var now = DateTimeOffset.UtcNow;
        var row = await db.ExternalDataCaches.FirstOrDefaultAsync(
            c => c.Provenance == provenance && c.CacheKey == spec.Key, ct);
        if (row is not null && row.ExpiresAt > now)
        {
            var value = JsonSerializer.Deserialize<T>(row.Payload)!;
            if (provenance == DataProvenance.Demo) return value;
            var evidence = NameFreeIRacingEvidence.Map(value);
            var cleaned = JsonSerializer.Serialize(evidence);
            if (cleaned != row.Payload)
            {
                // A name-bearing shared snapshot is not eligible for a warm-hit repair that could
                // bypass copy generations. It is withdrawn and physically removed by reconciliation.
                await new EvidenceCopyLifecycle(db, TimeProvider.System).WithdrawNameBearingCacheAsync(row.EvidenceCopyId!.Value, ct);
                throw new IRacingNotConfiguredException();
            }
            return evidence;
        }

        // A configured provider is irrelevant to a synthetic request, including a cold miss.
        if (provenance != DataProvenance.Real)
            throw new IRacingNotConfiguredException();
        var live = client ?? throw new IRacingNotConfiguredException();
        await RealAcquisitionGuard.EnsureDemoTeardownAsync(db, ct);
        var purposeId = await (purposeIssuer ?? new UnavailableEvidencePurposeIssuer()).ResolveAsync(
            new(provenance, spec.Purpose, spec.SeasonId), ct) ?? throw new IRacingNotConfiguredException();
        var http = httpContext?.HttpContext;
        var user = http?.User.Identity?.IsAuthenticated == true
            && Guid.TryParse(http.User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value, out var id) ? (Guid?)id : null;
        var request = http is null ? null : DriverOperatingAdmission.Request(http, spec.Key,
            $"{spec.Purpose}:{spec.SeasonId}", OperatingWork.Acquisition, provenance, user);
        if (request is null) throw new IRacingNotConfiguredException();
        var controls = operating ?? new UnavailableDriverOperatingControls();
        var lifecycle = new EvidenceCopyLifecycle(db, TimeProvider.System);
        // Capture only inside operating-admitted work, while the existing purpose/copy fence remains authoritative.
        EvidenceWriteReceipt? receipt = null;
        var fresh = NameFreeIRacingEvidence.Map(await new DriverOperatingCollection(controls).CollectAsync(request, async token =>
        {
            receipt = await lifecycle.CaptureAsync(purposeId, EvidenceCopyKind.MappedCache, spec.Key, ct: token);
            return await fetch(live);
        }, ct));
        await RealAcquisitionGuard.EnsureDemoTeardownAsync(db, ct);
        var json = JsonSerializer.Serialize(fresh);

        await lifecycle.CommitAsync(receipt!, new MappedCacheBatch(new ExternalDataCache
        {
            CacheKey = spec.Key,
            Provenance = provenance,
            Payload = json,
            FetchedAt = receipt!.OriginalAcquiredAt,
            ExpiresAt = receipt!.OriginalAcquiredAt + spec.Ttl,
        }), ct);

        return fresh;
    }
}

/// <summary>Thrown when the selected evidence scope is unavailable or cannot satisfy a read.</summary>
public sealed class IRacingNotConfiguredException()
    : Exception("iRacing integration is not configured on this server.");
