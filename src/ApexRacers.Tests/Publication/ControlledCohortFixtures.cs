using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using ApexRacers.Api.Services;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Lifecycle;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Tests.Publication;

internal static class CohortActors
{
    public static readonly Guid Owner = new("aaaaaaaa-3740-4000-8000-000000000001");
    public static readonly Guid OtherOwner = new("aaaaaaaa-3740-4000-8000-000000000002");
    public static readonly Guid Consenting = new("aaaaaaaa-3740-4000-8000-000000000007");
    public static readonly Guid Recipient = new("bbbbbbbb-3740-4000-8000-000000000001");
    public const string CacheKey = "controlled-publication-fixture-v1";
    public static Guid User(int id) => id switch { 1 => Owner, 2 => OtherOwner, 7 => Consenting, _ => Guid.Empty };
    public static ControlledCohortSource Source()
    {
        var members = Enumerable.Range(1, 7).Select(id => new SyntheticCohortMember(id,
            id == 7 ? "Synthetic Consenting Driver" : null, new(CandidateMeasurementState.Measured, 90.09m + id * .01m),
            new(CandidateMeasurementState.Measured, id == 7 ? 1350 : 1249 + id),
            id == 7 ? [CandidateAudience.SignedInPreview] : [], 1, 1)).ToImmutableArray();
        return new(new("publication-ledger-controlled-field", 1, DataProvenance.Demo, 1, 1, true, members),
            members.Select(m => new DriverScope(User(m.CustomerId), m.CustomerId, DataProvenance.Demo)).ToImmutableArray(),
            [new(CandidateAudience.Visitor), new(CandidateAudience.SignedInPreview),
                new(CandidateAudience.SignedInPreview, 1, 89.90m), new(CandidateAudience.SignedInPreview, 2, 89.91m)]);
    }

    public static async Task SeedEvidenceAsync(AppDbContext db, ControlledCohortSource source, CancellationToken ct)
    {
        var copies = new EvidenceCopyLifecycle(db, TimeProvider.System);
        var purpose = await copies.OpenSyntheticPurposeAsync(ct: ct);
        var receipt = await copies.CaptureAsync(purpose, EvidenceCopyKind.MappedCache, CacheKey, ct: ct);
        await copies.CommitAsync(receipt, new MappedCacheBatch(new ExternalDataCache
        {
            CacheKey = CacheKey,
            Payload = JsonSerializer.Serialize(source),
            FetchedAt = receipt.OriginalAcquiredAt,
            ExpiresAt = receipt.OriginalAcquiredAt.AddHours(1)
        }), ct);
    }
}

internal sealed class ControlledCohortSqlSource(AppDbContext db, LifecycleGates gates) : IControlledCohortSource
{
    public async Task<ControlledCohortSource?> LoadAsync(CancellationToken ct = default)
    {
        if (gates.IsFaulted("source-unavailable")) return null;
        var cache = await db.ExternalDataCaches.AsNoTracking().SingleOrDefaultAsync(c => c.CacheKey == CohortActors.CacheKey, ct);
        if (cache?.EvidenceCopyId is not { } id) return null;
        var marker = await db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(c => c.Id == id, ct);
        var purpose = await db.EvidencePurposes.AsNoTracking().SingleAsync(p => p.Id == marker.PurposeId, ct);
        if (marker.UnavailableAt is not null || marker.VerifiedRemovedAt is not null || purpose.OriginalEndedAt is not null
            || marker.Generation != purpose.Generation || marker.Provenance != DataProvenance.Demo || purpose.Provenance != DataProvenance.Demo) return null;
        var source = JsonSerializer.Deserialize<ControlledCohortSource>(cache.Payload)!;
        var grants = await db.DriverAuthorizationGrants.AsNoTracking().Where(g => g.Provenance == DataProvenance.Demo).ToDictionaryAsync(g => g.CustomerId, ct);
        var snapshot = source.Snapshot with
        {
            CohortRevision = marker.Version,
            EnforcementRevision = purpose.EvidenceVersion,
            Members = source.Snapshot.Members.Select(m => m with
            {
                EvidenceRevision = marker.Version,
                AuthorityRevision = grants.TryGetValue(m.CustomerId, out var grant) ? grant.Revision : 1,
                DriverName = grants.TryGetValue(m.CustomerId, out grant) && grant.SharingConsentVersion is not null ? grant.AuthorizedDriverName : null,
                SharingAudiences = grants.TryGetValue(m.CustomerId, out grant) && grant.SharingConsentVersion is not null ? [CandidateAudience.SignedInPreview] : []
            }).ToImmutableArray()
        };
        source = source with { Scopes = source.Scopes.Select(s => s with { UserId = grants.TryGetValue(s.CustomerId, out var g) ? g.UserId : Guid.Empty }).ToImmutableArray() };
        if (gates.IsFaulted("scope-mismatch")) source = source with { Scopes = source.Scopes.SetItem(6, new(Guid.Empty, 8, DataProvenance.Demo)) };
        if (gates.IsFaulted("forged-name")) snapshot = snapshot with { Members = snapshot.Members.SetItem(6, snapshot.Members[6] with { DriverName = "Incorrect synthetic label" }) };
        return source with
        {
            Snapshot = snapshot,
            Evidence = new(marker.Id, marker.Version, purpose.Id, purpose.Generation,
            purpose.EvidenceVersion, marker.OriginalAcquiredAt, purpose.CreatedAt)
        };
    }
}

internal sealed class FiniteCohortCatalog(LifecycleGates gates, ControlledCohortEvidence genesis) : IControlledCohortCatalog
{
    public Task<ControlledCandidateReview?> ReviewAsync(ControlledCohortSource source, CancellationToken ct = default)
    {
        var expected = CohortActors.Source() with { Evidence = genesis };
        if (gates.IsFaulted("catalog-unreviewed") || JsonSerializer.Serialize(source) != JsonSerializer.Serialize(expected))
            return Task.FromResult<ControlledCandidateReview?>(null);
        var variants = gates.IsFaulted("catalog-owner-removed")
            ? source.ReviewedVariants.Where(v => v.OwnerCustomerId is null).ToImmutableArray()
            : gates.IsFaulted("catalog-other-owner-removed")
                ? source.ReviewedVariants.Where(v => v.OwnerCustomerId != 2).ToImmutableArray() : source.ReviewedVariants;
        return Task.FromResult(WholeCohortCandidates.ReviewControlledSynthetic(source.Snapshot, variants, new(
            CandidateRiskVerdict.ControlledSyntheticSupported, CandidateRiskVerdict.ControlledSyntheticSupported,
            CandidateRiskVerdict.ControlledSyntheticSupported, CandidateRiskVerdict.ControlledSyntheticSupported)));
    }
}

/// <summary>Explicit immutable allowlisted synthetic release combinations. Hash equality is never
/// a general safety algorithm; every known signature and combination is declared in this fixture.</summary>
internal sealed class FiniteCompositionReview(LifecycleGates? gates = null, string? writer = null) : IPublicationCompositionReview
{
    public const string ReviewVersion = "controlled-composition-v1";
    private readonly ImmutableDictionary<(string Representation, PublicationPurpose Purpose, Guid? Recipient), string> signatures = Signatures();
    private readonly ImmutableArray<ImmutableHashSet<string>> combinations =
    [ImmutableHashSet.Create("visitor"), ImmutableHashSet.Create("signed"), ImmutableHashSet.Create("owner1"),
        ImmutableHashSet.Create("owner2"), ImmutableHashSet.Create("visitor", "signed")];

    private static ImmutableDictionary<(string Representation, PublicationPurpose Purpose, Guid? Recipient), string> Signatures()
    {
        var source = CohortActors.Source();
        var reviewed = WholeCohortCandidates.ReviewControlledSynthetic(source.Snapshot, source.ReviewedVariants, new(
            CandidateRiskVerdict.ControlledSyntheticSupported, CandidateRiskVerdict.ControlledSyntheticSupported,
            CandidateRiskVerdict.ControlledSyntheticSupported, CandidateRiskVerdict.ControlledSyntheticSupported));
        var builder = ImmutableDictionary.CreateBuilder<(string, PublicationPurpose, Guid?), string>();
        foreach (var variant in source.ReviewedVariants)
        {
            var candidate = WholeCohortCandidates.Prepare(WholeCohortCandidates.CatalogId, WholeCohortCandidates.Scope, source.Snapshot, reviewed, variant)!;
            var code = variant.OwnerCustomerId is { } id ? "owner" + id : variant.Audience == CandidateAudience.Visitor ? "visitor" : "signed";
            foreach (var offset in new[] { 0, 1 })
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    HiddenGroups = WholeCohortCandidates.PageHidden(candidate, offset, 20),
                    candidate.Projection.NamedRows,
                    candidate.Projection.OwnerAnalytics
                }, JsonSerializerOptions.Web);
                var hash = Convert.ToHexString(SHA256.HashData(bytes));
                if (variant.OwnerCustomerId is { } owner) builder[(hash, PublicationPurpose.Owner, CohortActors.User(owner))] = code;
                else if (variant.Audience == CandidateAudience.Visitor) builder[(hash, PublicationPurpose.Aggregate, null)] = code;
                else foreach (var recipient in new[] { CohortActors.Recipient, CohortActors.Owner, CohortActors.OtherOwner })
                    builder[(hash, PublicationPurpose.SignedIn, recipient)] = code;
            }
        }
        return builder.ToImmutable();
    }
    public async Task<bool> AssessAsync(PublicationProposal proposal, ImmutableArray<PublicationAccounting> history, CancellationToken ct = default)
    {
        var signaturesToReview = history.Where(h => !h.ProvenUnsent).Select(h => h.Proposal).Append(proposal).ToArray();
        if (signaturesToReview.Any(p => p.CatalogRevision != 1 || p.CatalogId != WholeCohortCandidates.CatalogId
            || !signatures.ContainsKey((p.RepresentationHash, p.Purpose, p.RecipientUserId)))) return false;
        var union = signaturesToReview.Select(p => signatures[(p.RepresentationHash, p.Purpose, p.RecipientUserId)]).ToImmutableHashSet();
        var accepted = combinations.Any(allowed => union.IsSubsetOf(allowed));
        if (gates is not null && writer is not null) await gates.ReachAsync(writer, "review-assessed", ct);
        return accepted;
    }
}
