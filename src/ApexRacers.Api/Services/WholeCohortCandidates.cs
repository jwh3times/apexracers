using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using ApexRacers.Core;

namespace ApexRacers.Api.Services;

public enum CandidateRiskVerdict { Unreviewed, Unsafe, ControlledSyntheticSupported }
public sealed record ControlledCandidateRiskReview(CandidateRiskVerdict FieldsScopeAudience,
    CandidateRiskVerdict ExternalLinkage, CandidateRiskVerdict PriorAndOverlappingReleases,
    CandidateRiskVerdict TotalsAndOwnerVariants);
public sealed record SyntheticCohortSnapshot(string CohortId, long CohortRevision, DataProvenance Provenance,
    long CatalogRevision, long EnforcementRevision, bool DependenciesCurrent,
    ImmutableArray<SyntheticCohortMember> Members);
public sealed record WholeCohortCandidateArtifact(string SchemaVersion, string CatalogId, string Scope,
    string CohortId, long CohortRevision, DataProvenance Provenance, CandidateVariant Variant,
    bool ReviewOnly, bool Admitted, WholeCohortProjection Projection);

/// <summary>An immutable controlled fixture assessment. It is never maintainer catalog approval.</summary>
public sealed class ControlledCandidateReview
{
    // Keep receipt construction restricted to the controlled review factory.
    internal ControlledCandidateReview(string fingerprint, ImmutableArray<CandidateVariant> variants)
    {
        Fingerprint = fingerprint;
        Variants = variants;
    }
    internal string Fingerprint { get; }
    internal ImmutableArray<CandidateVariant> Variants { get; }
}

/// <summary>Offline review artifacts only: no production registration, authorization, admission,
/// HTTP result, release reservation or protected dispatch. #374 must own combined-release admission.</summary>
public static class WholeCohortCandidates
{
    public const string SchemaVersion = "whole-cohort-candidate-v1";
    public const string CatalogId = "synthetic-lap-rating-v1";
    public const string Scope = "synthetic-single-field-v1";

    public static ControlledCandidateReview? ReviewControlledSynthetic(SyntheticCohortSnapshot snapshot,
        ImmutableArray<CandidateVariant> variants, ControlledCandidateRiskReview assessment)
    {
        if (!Eligible(snapshot) || !WholeCohortBands.InputsValid(snapshot.Members, variants)
            || assessment.FieldsScopeAudience != CandidateRiskVerdict.ControlledSyntheticSupported
            || assessment.ExternalLinkage != CandidateRiskVerdict.ControlledSyntheticSupported
            || assessment.PriorAndOverlappingReleases != CandidateRiskVerdict.ControlledSyntheticSupported
            || assessment.TotalsAndOwnerVariants != CandidateRiskVerdict.ControlledSyntheticSupported) return null;
        return new(Fingerprint(snapshot, variants), variants);
    }

    public static WholeCohortCandidateArtifact? Prepare(string catalogId, string scope, SyntheticCohortSnapshot snapshot,
        ControlledCandidateReview? review, CandidateVariant variant)
    {
        if (catalogId != CatalogId || scope != Scope || review is null || !Eligible(snapshot)
            || !WholeCohortBands.InputsValid(snapshot.Members, review.Variants)
            || review.Fingerprint != Fingerprint(snapshot, review.Variants)) return null;
        WholeCohortProjection? projection;
        try { projection = WholeCohortBands.Prepare(snapshot.Members, review.Variants, variant); }
        catch (OverflowException) { return null; }
        return projection is null ? null : new(SchemaVersion, CatalogId, Scope, snapshot.CohortId,
            snapshot.CohortRevision, snapshot.Provenance, variant, true, false, projection);
    }

    /// <summary>Page an already prepared candidate. A page cannot affect complete-cohort support.</summary>
    public static ImmutableArray<HiddenBandGroup> PageHidden(WholeCohortCandidateArtifact candidate, int offset, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(limit);
        return candidate.Projection.HiddenGroups.Skip(offset).Take(limit).ToImmutableArray();
    }

    private static bool Eligible(SyntheticCohortSnapshot snapshot) => snapshot.Provenance == DataProvenance.Demo
        && snapshot.DependenciesCurrent && snapshot.CohortRevision > 0 && snapshot.CatalogRevision > 0
        && snapshot.EnforcementRevision > 0 && !string.IsNullOrWhiteSpace(snapshot.CohortId) && !snapshot.Members.IsDefaultOrEmpty;

    private static string Fingerprint(SyntheticCohortSnapshot snapshot, ImmutableArray<CandidateVariant> variants)
    {
        // Sorting removes input/page order; the complete normalized evidence and consent revisions
        // remain dependencies. Unsupported numeric values do not become published measurements.
        var canonical = new
        {
            SchemaVersion,
            CatalogId,
            Scope,
            snapshot.CohortId,
            snapshot.CohortRevision,
            snapshot.Provenance,
            snapshot.CatalogRevision,
            snapshot.EnforcementRevision,
            snapshot.DependenciesCurrent,
            Variants = variants.OrderBy(v => v.Audience).ThenBy(v => v.OwnerCustomerId).ThenBy(v => v.OwnerPersonalBestLapSeconds).ToArray(),
            Members = snapshot.Members.OrderBy(m => m.CustomerId).ThenBy(m => m.LapSeconds.Value)
                .ThenBy(m => m.AbsoluteRating.Value).Select(m => new
                {
                    m.CustomerId,
                    m.DriverName,
                    m.LapSeconds,
                    m.AbsoluteRating,
                    m.AuthorityRevision,
                    m.EvidenceRevision,
                    SharingAudiences = m.SharingAudiences.Order().ToArray(),
                }).ToArray(),
        };
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(canonical)));
    }
}
