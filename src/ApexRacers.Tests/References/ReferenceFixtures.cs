using System.Collections.Immutable;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using ApexRacers.Api.Services;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Lifecycle;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace ApexRacers.Tests.References;

internal static class ReferenceActors
{
    public static readonly Guid Recipient = new("aaaaaaaa-3750-4000-8000-000000000001");
    public static readonly Guid Target = new("aaaaaaaa-3750-4000-8000-000000000002");
    public static readonly Guid Other = new("aaaaaaaa-3750-4000-8000-000000000003");
    public const string Key = "controlled-reference-evidence-v1";
    public static DriverScope Scope(Guid user) => new(user, user == Recipient ? 1 : user == Target ? 2 : 3, DataProvenance.Demo);
    public static IConfiguration Configuration => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["JWT_SIGNING_KEY"] = "controlled375-synthetic-only-key-32bytes-long",
        ["JWT_ISSUER"] = "controlled-reference-host",
        ["JWT_AUDIENCE"] = "controlled-reference-clients"
    }).Build();
    public static string Token(Guid user)
    {
        var settings = JwtSettings.FromConfiguration(Configuration);
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(settings.Issuer, settings.Audience,
            [new Claim(JwtRegisteredClaimNames.Sub, user.ToString())], expires: DateTime.UtcNow.AddMinutes(15), signingCredentials: settings.IssuingCredentials()));
    }
    public static ImmutableArray<ControlledDriverDetail> Details => [new(Scope(Recipient), 89.90, 1500), new(Scope(Target), 90.16, 1350)];
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct, bool initializeGenesis = true)
    {
        var lifecycle = new EvidenceCopyLifecycle(db, TimeProvider.System);
        var purpose = await lifecycle.OpenSyntheticPurposeAsync(ct: ct);
        var receipt = await lifecycle.CaptureAsync(purpose, EvidenceCopyKind.MappedCache, Key, ct: ct);
        await lifecycle.CommitAsync(receipt, new MappedCacheBatch(new ExternalDataCache
        {
            CacheKey = Key,
            Payload = JsonSerializer.Serialize(Details),
            FetchedAt = receipt.OriginalAcquiredAt,
            ExpiresAt = receipt.OriginalAcquiredAt.AddDays(200)
        }), ct);
        if (!initializeGenesis) return;
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE controlled_reference_genesis(copy_id uuid PRIMARY KEY)", ct);
        var seeded = await db.ExternalDataCaches.SingleAsync(c => c.CacheKey == Key, ct);
        await db.Database.ExecuteSqlAsync($"INSERT INTO controlled_reference_genesis VALUES ({seeded.EvidenceCopyId})", ct);
    }
}
internal sealed class ReferenceClock : TimeProvider
{
    private long ticks = DateTimeOffset.UtcNow.UtcTicks;
    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
    public void Set(DateTimeOffset time) => Interlocked.Exchange(ref ticks, time.UtcTicks);
}
internal sealed class ReferenceCatalog(AppDbContext db, ControlledCohortEvidence genesis, LifecycleGates gates, bool browser = false) : IControlledDriverReferenceCatalog
{
    public async Task<ControlledDriverReferenceSource?> LoadAsync(CancellationToken ct = default)
    {
        if (gates.IsFaulted("reference-source-unavailable")) return null;
        var cache = await db.ExternalDataCaches.AsNoTracking().SingleOrDefaultAsync(c => c.CacheKey == ReferenceActors.Key, ct);
        if (cache?.EvidenceCopyId is not { } id) return null;
        var marker = await db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(m => m.Id == id, ct);
        var purpose = await db.EvidencePurposes.AsNoTracking().SingleAsync(p => p.Id == marker.PurposeId, ct);
        return new(JsonSerializer.Deserialize<ImmutableArray<ControlledDriverDetail>>(cache.Payload),
            new(marker.Id, marker.Version, purpose.Id, purpose.Generation, purpose.EvidenceVersion, marker.OriginalAcquiredAt, purpose.CreatedAt));
    }
    public Task<string?> ReviewAsync(ControlledDriverReferenceSource source, CancellationToken ct = default)
    {
        var expected = new ControlledDriverReferenceSource(ReferenceActors.Details, genesis);
        // Original source/receipt, full exact fabricated values, schema and generation remain bound.
        return Task.FromResult<string?>(!gates.IsFaulted("reference-catalog-unavailable")
            && JsonSerializer.Serialize(source) == JsonSerializer.Serialize(expected)
            ? (browser ? "controlled-browser-template-v1:" : "controlled-reference-template-v1:") + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(expected))) : null);
    }
}
internal sealed class ReferenceCompositionReview : IPublicationCompositionReview
{
    public Task<bool> AssessAsync(PublicationProposal proposal, ImmutableArray<PublicationAccounting> history, CancellationToken ct = default)
    {
        // This is an independently fabricated bounded template, not #30 admission. Only its
        // exact recipient, original scopes, named target/owner dependencies and catalog are declared.
        bool Declared(PublicationProposal p) => p.CatalogId == DriverReferences.CatalogId && p.CatalogRevision == 1
            && p.Provenance == DataProvenance.Demo && p.Purpose == PublicationPurpose.SignedIn && p.RecipientUserId == ReferenceActors.Recipient
            && p.Dependencies.Length == 2 && p.Dependencies.Any(d => d.Scope == ReferenceActors.Scope(ReferenceActors.Recipient) && d.RequiredPurpose == DriverConsentScope.Personal)
            && p.Dependencies.Any(d => d.Scope == ReferenceActors.Scope(ReferenceActors.Target) && d.RequiredPurpose == DriverConsentScope.Sharing);
        return Task.FromResult(Declared(proposal) && history.Where(h => !h.ProvenUnsent).All(h => Declared(h.Proposal)));
    }
}

/// <summary>Separate, explicitly fabricated browser template. Does not widen the #375 review.</summary>
internal sealed class BrowserCompositionReview : IPublicationCompositionReview
{
    public Task<bool> AssessAsync(PublicationProposal proposal, ImmutableArray<PublicationAccounting> history, CancellationToken ct = default)
    {
        bool Declared(PublicationProposal p) => p.CatalogId == DriverReferences.CatalogId && p.CatalogRevision == 1
            && p.Provenance == DataProvenance.Demo && p.Purpose == PublicationPurpose.SignedIn
            && p.RecipientUserId is { } user && (user == ReferenceActors.Recipient || user == ReferenceActors.Target)
            && (p.Dependencies.Length == 1 && p.Dependencies[0].Scope == ReferenceActors.Scope(user) && p.Dependencies[0].RequiredPurpose == DriverConsentScope.Personal
                || user == ReferenceActors.Recipient && p.Dependencies.Length == 2
                && p.Dependencies.Any(d => d.Scope == ReferenceActors.Scope(user) && d.RequiredPurpose == DriverConsentScope.Personal)
                && p.Dependencies.Any(d => d.Scope == ReferenceActors.Scope(ReferenceActors.Target) && d.RequiredPurpose == DriverConsentScope.Sharing));
        return Task.FromResult(Declared(proposal) && history.Where(h => !h.ProvenUnsent).All(h => Declared(h.Proposal)));
    }
}
