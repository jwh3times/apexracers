using System.Collections.Immutable;
using System.Text.Json;
using ApexRacers.Api.Services;
using ApexRacers.Core;
using Xunit;

namespace ApexRacers.Tests.Services;

public sealed class WholeCohortCandidateTests
{
    private static readonly CandidateVariant Visitor = new(CandidateAudience.Visitor);
    private static readonly ControlledCandidateRiskReview Supported = new(
        CandidateRiskVerdict.ControlledSyntheticSupported, CandidateRiskVerdict.ControlledSyntheticSupported,
        CandidateRiskVerdict.ControlledSyntheticSupported, CandidateRiskVerdict.ControlledSyntheticSupported);

    [Theory]
    [InlineData("999.999", "500", "1000")]
    [InlineData("1000", "1000", "1250")]
    [InlineData("1249.999", "1000", "1250")]
    [InlineData("1250", "1250", "1500")]
    [InlineData("2499.999", "2250", "2500")]
    [InlineData("2500", "2500", "3000")]
    [InlineData("3000", "3000", "3500")]
    [InlineData("-0.001", "-500", "0")]
    [InlineData("2249.9999999999999999999999999", "2000", "2250")]
    [InlineData("-0.0000000000000000000000000001", "-500", "0")]
    public void BaseAbsoluteRatingHasExactHalfOpenZeroOrigin(string value, string lower, string upper) =>
        Assert.Equal(new CandidateRange(D(lower), D(upper)), WholeCohortBands.RatingRange(D(value), 0));

    [Theory]
    [InlineData(0, "90.499999", "90", "90.5")]
    [InlineData(0, "90.5", "90.5", "91")]
    [InlineData(1, "91", "91", "92")]
    [InlineData(2, "92", "92", "94")]
    [InlineData(3, "92", "92", "96")]
    [InlineData(4, "96", "96", "104")]
    [InlineData(3, "3.9999999999999999999999999999", "0", "4")]
    [InlineData(0, "-0.0000000000000000000000000001", "-0.5", "0")]
    public void LapBandsHaveExactBoundaries(int level, string value, string lower, string upper) =>
        Assert.Equal(new CandidateRange(D(lower), D(upper)), WholeCohortBands.LapRange(D(value), level));

    [Fact]
    public void EveryBaseBoundaryNestsInEveryCoarserRatingPartition()
    {
        foreach (var value in new[] { -1m, 0m, 999m, 1000m, 1249m, 1250m, 2499m, 2500m, 2999m, 3000m, 7999m, 8000m })
        {
            var fine = WholeCohortBands.RatingRange(value, 0);
            for (var level = 1; level <= 4; level++)
            {
                var coarse = WholeCohortBands.RatingRange(value, level);
                Assert.True(coarse.LowerInclusive <= fine.LowerInclusive && coarse.UpperExclusive >= fine.UpperExclusive);
                fine = coarse;
            }
        }
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    public void DistinctHiddenSupportBoundary(int count, bool supported) =>
        Assert.Equal(supported, Candidate(Snapshot(Members(count))) is not null);

    [Fact]
    public void RepeatedDriverRowsNeverIncreaseSupport()
    {
        var four = Members(4);
        Assert.Null(Candidate(Snapshot([.. four, .. four, .. four])));
        var five = Members(5);
        var artifact = Candidate(Snapshot([.. five, .. five]));
        Assert.Single(Assert.IsType<WholeCohortCandidateArtifact>(artifact).Projection.HiddenGroups);
        Assert.Null(Candidate(Snapshot([.. five, five[0] with { LapSeconds = Measured(89m) }])));
    }

    [Fact]
    public void OneGlobalLevelWidensEvenGroupsThatAlreadyHadBaseSupport()
    {
        var rows = Members(10).Select(m => m.CustomerId <= 5 ? m : m with
        {
            LapSeconds = Measured(m.CustomerId <= 7 ? 92.1m : 92.6m),
        }).ToImmutableArray();
        var artifact = Assert.IsType<WholeCohortCandidateArtifact>(Candidate(Snapshot(rows)));
        Assert.Equal(1, artifact.Projection.Level);
        Assert.Equal([new(90m, 91m), new CandidateRange(92m, 93m)], artifact.Projection.HiddenGroups.Select(g => g.LapSeconds));
        Assert.Equal(artifact.Projection.HiddenGroups[1], Assert.Single(WholeCohortCandidates.PageHidden(artifact, 1, 1)));
        Assert.Empty(WholeCohortCandidates.PageHidden(artifact, 99, 2));
        Assert.Equal(1, artifact.Projection.Level);
    }

    [Theory]
    [InlineData("90.6", 1)]
    [InlineData("91.1", 2)]
    [InlineData("91.9", 2)]
    [InlineData("89.9", 3)]
    [InlineData("95.1", 4)]
    public void FinestSupportedLevelIsChosen(string otherLap, int expected)
    {
        var rows = Members(5).Select(m => m.CustomerId <= 2 ? m : m with { LapSeconds = Measured(D(otherLap)) }).ToImmutableArray();
        Assert.Equal(expected, Assert.IsType<WholeCohortCandidateArtifact>(Candidate(Snapshot(rows))).Projection.Level);
    }

    [Fact]
    public void UnsupportedSparseGroupsCannotBeRescuedByIndependentStatistics()
    {
        var sparse = Snapshot(Members(6).Select(m => m.CustomerId == 6 ? m with { LapSeconds = Measured(104m) } : m).ToImmutableArray());
        Assert.Null(Candidate(sparse));
    }

    [Fact]
    public void UnsupportedMeasurementsAreOmittedAndVisibleMissingnessNeedsJointSupport()
    {
        var unsupported = new CandidateMeasurement(CandidateMeasurementState.Unsupported, 123456789m);
        var rows = Members(10).Select(m => m.CustomerId <= 5 ? m : m with { AbsoluteRating = unsupported }).ToImmutableArray();
        var groups = Assert.IsType<WholeCohortCandidateArtifact>(Candidate(Snapshot(rows))).Projection.HiddenGroups;
        Assert.Equal(2, groups.Length);
        Assert.Single(groups.Where(g => g.AbsoluteRating is null));
        Assert.DoesNotContain("123456789", JsonSerializer.Serialize(groups));
        Assert.Null(Candidate(Snapshot(Members(6).Select(m => m.CustomerId == 6 ? m with { AbsoluteRating = unsupported } : m).ToImmutableArray())));
        Assert.Null(Candidate(Snapshot(Members(5).Select(m => m with { LapSeconds = unsupported, AbsoluteRating = unsupported }).ToImmutableArray())));
    }

    [Fact]
    public void AllReviewedOwnersUseOneLevelAndFiveToFourCannotRechoose()
    {
        var variants = Enumerable.Range(1, 6).Select(id => new CandidateVariant(CandidateAudience.SignedInPreview, id)).ToImmutableArray();
        var six = Snapshot(Members(6));
        var review = WholeCohortCandidates.ReviewControlledSynthetic(six, variants, Supported);
        foreach (var variant in variants)
            Assert.Equal(0, Assert.IsType<WholeCohortCandidateArtifact>(Prepare(six, review, variant)).Projection.Level);
        var five = Snapshot(Members(5));
        var fiveVariants = variants.RemoveAt(5);
        var fiveReview = WholeCohortCandidates.ReviewControlledSynthetic(five, fiveVariants, Supported);
        Assert.Null(Prepare(five, fiveReview, fiveVariants[0]));
        Assert.Null(Prepare(six, review, new(CandidateAudience.SignedInOther, 1)));
    }

    [Fact]
    public void WholeVariantReviewCannotReuseVisitorFiveDriverSupportForOwner()
    {
        var rows = Snapshot(Members(5));
        Assert.NotNull(Candidate(rows));
        var owner = new CandidateVariant(CandidateAudience.SignedInPreview, 1);
        var review = WholeCohortCandidates.ReviewControlledSynthetic(rows, [Visitor, owner], Supported);
        Assert.Null(Prepare(rows, review, Visitor));
        Assert.Null(Prepare(rows, review, owner));
    }

    [Fact]
    public void NamesOnlyAppearInTheirPermittedSignedInAudienceAndNeverBoostHiddenSupport()
    {
        var rows = Members(7).Select(m => m.CustomerId == 7 ? m with { DriverName = "Consented synthetic", SharingAudiences = [CandidateAudience.SignedInPreview] } : m).ToImmutableArray();
        var snapshot = Snapshot(rows);
        CandidateVariant[] variants = [Visitor, new(CandidateAudience.SignedInPreview, 1), new(CandidateAudience.SignedInOther, 1)];
        var review = WholeCohortCandidates.ReviewControlledSynthetic(snapshot, [.. variants], Supported);
        Assert.Empty(Assert.IsType<WholeCohortCandidateArtifact>(Prepare(snapshot, review, Visitor)).Projection.NamedRows);
        Assert.Equal("Consented synthetic", Assert.Single(Assert.IsType<WholeCohortCandidateArtifact>(Prepare(snapshot, review, variants[1])).Projection.NamedRows).DriverName);
        Assert.Empty(Assert.IsType<WholeCohortCandidateArtifact>(Prepare(snapshot, review, variants[2])).Projection.NamedRows);
        var tooFew = Snapshot(rows.RemoveAt(5));
        Assert.Null(Prepare(tooFew, WholeCohortCandidates.ReviewControlledSynthetic(tooFew, [.. variants], Supported), variants[1]));
    }

    [Fact]
    public void OwnerRankKeepsExactFormulaAndPersonalBestDoesNotDoubleCountRaceBest()
    {
        decimal[] laps = [90.4m, 90.1m, 90.2m, 90.3m, 90.3m, 90.4m, 90.4m];
        var snapshot = Snapshot(Members(7).Select((m, i) => m with { LapSeconds = Measured(laps[i]) }).ToImmutableArray());
        var variant = new CandidateVariant(CandidateAudience.SignedInPreview, 1, 90.2m);
        var review = WholeCohortCandidates.ReviewControlledSynthetic(snapshot, [variant], Supported);
        var analytics = Assert.IsType<CandidateOwnerAnalytics>(Assert.IsType<WholeCohortCandidateArtifact>(Prepare(snapshot, review, variant)).Projection.OwnerAnalytics);
        Assert.Equal(5d / 7d * 100d, analytics.PercentileRank, 10);
        Assert.Equal(FieldPercentile.Rank(90.2d, laps.Skip(1).Select(l => (double)l).ToArray()), analytics.PercentileRank);
        Assert.Equal(["PercentileRank"], analytics.GetType().GetProperties().Select(p => p.Name));
        Assert.Null(Prepare(snapshot, review, variant with { OwnerPersonalBestLapSeconds = 90.1m }));
    }

    [Fact]
    public void HiddenPublicShapeContainsOnlyRangesWithoutCountIdentityOrExactMetric()
    {
        var groups = Assert.IsType<WholeCohortCandidateArtifact>(Candidate(Snapshot(Members(5)))).Projection.HiddenGroups;
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(groups, JsonSerializerOptions.Web));
        var group = Assert.Single(json.RootElement.EnumerateArray());
        Assert.Equal(["lapSeconds", "absoluteRating"], group.EnumerateObject().Select(p => p.Name));
        foreach (var range in group.EnumerateObject())
            Assert.Equal(["lowerInclusive", "upperExclusive"], range.Value.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public void ReviewContextIsClosedForUnknownRealStaleScopeOrUnlistedVariant()
    {
        var snapshot = Snapshot(Members(5));
        var review = WholeCohortCandidates.ReviewControlledSynthetic(snapshot, [Visitor], Supported);
        Assert.Null(WholeCohortCandidates.Prepare("unknown", WholeCohortCandidates.Scope, snapshot, review, Visitor));
        Assert.Null(WholeCohortCandidates.Prepare(WholeCohortCandidates.CatalogId, "custom-filter", snapshot, review, Visitor));
        Assert.Null(Prepare(snapshot, null, Visitor));
        Assert.Null(Prepare(snapshot with { Provenance = DataProvenance.Real }, review, Visitor));
        Assert.Null(Prepare(snapshot with { DependenciesCurrent = false }, review, Visitor));
        Assert.Null(Prepare(snapshot, review, new(CandidateAudience.SignedInPreview)));
    }

    [Fact]
    public void EveryDependencyChangeInvalidatesEntirePreparedReview()
    {
        var snapshot = Snapshot(Members(6));
        var review = WholeCohortCandidates.ReviewControlledSynthetic(snapshot, [Visitor], Supported);
        SyntheticCohortSnapshot[] changes =
        [
            snapshot with { CohortRevision = 2 }, snapshot with { CatalogRevision = 2 },
            snapshot with { EnforcementRevision = 2 }, snapshot with { CohortId = "other-field" },
            snapshot with { Members = snapshot.Members.RemoveAt(5) },
            snapshot with { Members = snapshot.Members.SetItem(0, snapshot.Members[0] with { EvidenceRevision = 2 }) },
            snapshot with { Members = snapshot.Members.SetItem(0, snapshot.Members[0] with { LapSeconds = Measured(90.11m) }) },
            snapshot with { Members = snapshot.Members.SetItem(0, snapshot.Members[0] with { AuthorityRevision = 2 }) },
            snapshot with { Members = snapshot.Members.SetItem(0, snapshot.Members[0] with { DriverName = "now disclosed", SharingAudiences = [CandidateAudience.SignedInPreview] }) },
        ];
        foreach (var changed in changes) Assert.Null(Prepare(changed, review, Visitor));
        Assert.NotNull(Prepare(snapshot with { Members = [.. snapshot.Members.Reverse()] }, review, Visitor));
    }

    [Fact]
    public void ExternalLinkageOverlapsTotalsAndUnreviewedRisksCannotSelfApprove()
    {
        var snapshot = Snapshot(Members(6));
        ControlledCandidateRiskReview[] failures =
        [
            Supported with { FieldsScopeAudience = CandidateRiskVerdict.Unreviewed },
            Supported with { ExternalLinkage = CandidateRiskVerdict.Unsafe },
            Supported with { PriorAndOverlappingReleases = CandidateRiskVerdict.Unreviewed },
            Supported with { PriorAndOverlappingReleases = CandidateRiskVerdict.Unsafe },
            Supported with { TotalsAndOwnerVariants = CandidateRiskVerdict.Unsafe },
        ];
        foreach (var risk in failures) Assert.Null(WholeCohortCandidates.ReviewControlledSynthetic(snapshot, [Visitor], risk));
        var positive = Assert.IsType<WholeCohortCandidateArtifact>(Candidate(snapshot));
        Assert.True(positive.ReviewOnly);
        Assert.False(positive.Admitted);
        Assert.Equal("whole-cohort-candidate-v1", positive.SchemaVersion);
        Assert.NotNull(Candidate(snapshot)); // repeated preparation is evidence, never a reserved release
        Assert.DoesNotContain(typeof(Microsoft.AspNetCore.Mvc.IActionResult), positive.GetType().GetInterfaces());
    }

    [Fact]
    public void MalformedNormalizedInputsAndFabricatedReviewsRemainClosed()
    {
        var snapshot = Snapshot(Members(5));
        Assert.Empty(typeof(ControlledCandidateReview).GetConstructors());
        SyntheticCohortSnapshot[] invalid =
        [
            snapshot with { Members = default },
            snapshot with { Members = [null!] },
            snapshot with { Members = snapshot.Members.SetItem(0, snapshot.Members[0] with { LapSeconds = null! }) },
            snapshot with { Members = snapshot.Members.SetItem(0, snapshot.Members[0] with { AbsoluteRating = null! }) },
            snapshot with { Members = snapshot.Members.SetItem(0, snapshot.Members[0] with { SharingAudiences = default }) },
            snapshot with { Members = snapshot.Members.SetItem(0, snapshot.Members[0] with { SharingAudiences = [CandidateAudience.Visitor] }) },
            snapshot with { Members = snapshot.Members.SetItem(0, snapshot.Members[0] with { AuthorityRevision = 0 }) },
            snapshot with { Members = snapshot.Members.SetItem(0, snapshot.Members[0] with { LapSeconds = new((CandidateMeasurementState)999) }) },
            snapshot with { Members = [.. snapshot.Members, snapshot.Members[0] with { EvidenceRevision = 2 }] },
        ];
        var review = WholeCohortCandidates.ReviewControlledSynthetic(snapshot, [Visitor], Supported);
        foreach (var input in invalid)
        {
            Assert.Null(WholeCohortCandidates.ReviewControlledSynthetic(input, [Visitor], Supported));
            Assert.Null(Prepare(input, review, Visitor));
        }
        Assert.Null(WholeCohortCandidates.ReviewControlledSynthetic(snapshot, default, Supported));
        Assert.Null(WholeCohortCandidates.ReviewControlledSynthetic(snapshot, [null!], Supported));
        Assert.Null(WholeCohortCandidates.ReviewControlledSynthetic(snapshot, [new((CandidateAudience)999)], Supported));
        Assert.Null(WholeCohortCandidates.ReviewControlledSynthetic(snapshot, [new(CandidateAudience.Visitor, 1)], Supported));
        Assert.Null(WholeCohortCandidates.ReviewControlledSynthetic(snapshot, [new(CandidateAudience.SignedInPreview, null, 90m)], Supported));
    }

    [Fact]
    public void VersionedRiskArtifactFixtureProducesUsefulRangesNamesAndUnchangedOwnerRank()
    {
        var hidden = Members(6).Select((m, i) => m with { LapSeconds = Measured(90.10m + i * 0.01m), AbsoluteRating = Measured(1250m + i) });
        var consenting = new SyntheticCohortMember(7, "Synthetic Consenting Driver", Measured(90.20m),
            Measured(1350m), [CandidateAudience.SignedInPreview], 1, 1);
        var snapshot = Snapshot([.. hidden, consenting]) with { CohortId = "catalog-v1-useful-synthetic-field" };
        CandidateVariant[] variants = [Visitor, new(CandidateAudience.SignedInPreview), new(CandidateAudience.SignedInPreview, 1, 89.90m)];
        var review = WholeCohortCandidates.ReviewControlledSynthetic(snapshot, [.. variants], Supported);
        foreach (var variant in variants)
        {
            var artifact = Assert.IsType<WholeCohortCandidateArtifact>(Prepare(snapshot, review, variant));
            Assert.Equal(0, artifact.Projection.Level);
            Assert.Equal(new HiddenBandGroup(new(90m, 90.5m), new(1250m, 1500m)), Assert.Single(artifact.Projection.HiddenGroups));
            if (variant.Audience == CandidateAudience.Visitor) Assert.Empty(artifact.Projection.NamedRows);
            else Assert.Equal("Synthetic Consenting Driver", Assert.Single(artifact.Projection.NamedRows).DriverName);
            if (variant.OwnerCustomerId is not null) Assert.Equal(92.85714285714286d, artifact.Projection.OwnerAnalytics!.PercentileRank, 12);
            else Assert.Null(artifact.Projection.OwnerAnalytics);
            Assert.False(artifact.Admitted);
        }
    }

    [Fact]
    public void SupportedMarginalsCannotRescueAnUnsupportedJointVector()
    {
        var rows = Members(16).Select(m => m with
        {
            LapSeconds = Measured(m.CustomerId <= 8 ? 90.1m : 106.1m),
            AbsoluteRating = Measured(m.CustomerId % 8 < 4 ? 1200m : 9200m),
        }).ToImmutableArray();
        Assert.All(rows.GroupBy(m => m.LapSeconds.Value), g => Assert.Equal(8, g.Count()));
        Assert.All(rows.GroupBy(m => m.AbsoluteRating.Value), g => Assert.Equal(8, g.Count()));
        Assert.All(rows.GroupBy(m => (m.LapSeconds.Value, m.AbsoluteRating.Value)), g => Assert.Equal(4, g.Count()));
        Assert.Null(Candidate(Snapshot(rows)));
        Assert.Null(Candidate(Snapshot(Members(5).Select(m => m.CustomerId == 5 ? m with { LapSeconds = Measured(87.1m) } : m).ToImmutableArray())));
    }

    [Fact]
    public void KnownExternalClassificationCanIdentifyOneDriverDespiteFiveSupport()
    {
        var snapshot = Snapshot(Members(5));
        var artifact = Assert.IsType<WholeCohortCandidateArtifact>(Candidate(snapshot));
        var range = Assert.Single(artifact.Projection.HiddenGroups).LapSeconds!;
        // External racing context need not list all private contributors. Here only one known
        // public classified Driver matches the published tuple, creating a linkage hypothesis.
        (string Driver, decimal Lap, decimal Rating)[] external =
        [("Synthetic public driver A", 90.1m, 1200m), ("Synthetic public driver B", 95.1m, 3000m)];
        var linked = external.Where(e => e.Lap >= range.LowerInclusive && e.Lap < range.UpperExclusive
            && e.Rating >= 1000m && e.Rating < 1250m).ToArray();
        Assert.Equal("Synthetic public driver A", Assert.Single(linked).Driver);
        Assert.Null(WholeCohortCandidates.ReviewControlledSynthetic(snapshot, [Visitor],
            Supported with { ExternalLinkage = CandidateRiskVerdict.Unsafe }));
    }

    [Fact]
    public void RepeatedSupportedCandidatesExposeChangedRangesAndRequireFreshCompositionReview()
    {
        var initial = Snapshot(Members(10).Select(m => m.CustomerId <= 5 ? m : m with { LapSeconds = Measured(90.6m) }).ToImmutableArray());
        var originalReview = WholeCohortCandidates.ReviewControlledSynthetic(initial, [Visitor], Supported);
        var first = Assert.IsType<WholeCohortCandidateArtifact>(Prepare(initial, originalReview, Visitor));
        Assert.Equal(0, first.Projection.Level);
        Assert.Equal(2, first.Projection.HiddenGroups.Length);
        // Public knowledge that only synthetic Driver1 corrected a lap links the coarsening
        // between versions. Each separate candidate has >=5 support; composition still matters.
        var changed = initial with
        {
            CohortRevision = 2,
            Members = initial.Members.SetItem(0, initial.Members[0] with { LapSeconds = Measured(90.6m), EvidenceRevision = 2 }),
        };
        var second = Assert.IsType<WholeCohortCandidateArtifact>(Candidate(changed));
        Assert.Equal(1, second.Projection.Level);
        Assert.Equal(new CandidateRange(90m, 91m), Assert.Single(second.Projection.HiddenGroups).LapSeconds);
        Assert.Null(Prepare(changed, originalReview, Visitor));
        Assert.Null(WholeCohortCandidates.ReviewControlledSynthetic(changed, [Visitor],
            Supported with { PriorAndOverlappingReleases = CandidateRiskVerdict.Unreviewed }));
        Assert.Null(WholeCohortCandidates.ReviewControlledSynthetic(changed, [Visitor],
            Supported with { PriorAndOverlappingReleases = CandidateRiskVerdict.Unsafe }));
    }

    [Theory]
    [InlineData("1500", 1)]
    [InlineData("500", 2)]
    [InlineData("2200", 3)]
    [InlineData("5000", 4)]
    public void AbsoluteRatingAlsoSelectsTheFinestGlobalJointLevel(string rating, int expected)
    {
        var rows = Members(5).Select(m => m.CustomerId <= 2 ? m : m with { AbsoluteRating = Measured(D(rating)) }).ToImmutableArray();
        Assert.Equal(expected, Assert.IsType<WholeCohortCandidateArtifact>(Candidate(Snapshot(rows))).Projection.Level);
    }

    [Fact]
    public void DeclaredOwnerVariantCoarsensVisitorAndOwnerTogether()
    {
        var snapshot = Snapshot(Members(10).Select(m => m.CustomerId <= 5 ? m : m with { LapSeconds = Measured(90.6m) }).ToImmutableArray());
        var owner = new CandidateVariant(CandidateAudience.SignedInPreview, 1);
        var review = WholeCohortCandidates.ReviewControlledSynthetic(snapshot, [Visitor, owner], Supported);
        var visitorArtifact = Assert.IsType<WholeCohortCandidateArtifact>(Prepare(snapshot, review, Visitor));
        var ownerArtifact = Assert.IsType<WholeCohortCandidateArtifact>(Prepare(snapshot, review, owner));
        Assert.Equal(1, visitorArtifact.Projection.Level);
        Assert.Equal(visitorArtifact.Projection.Level, ownerArtifact.Projection.Level);
        Assert.Equal(visitorArtifact.Projection.HiddenGroups.ToArray(), ownerArtifact.Projection.HiddenGroups.ToArray());
    }

    [Fact]
    public void UnknownProvenanceRevisionsAndRiskVerdictsNeverGetControlledReview()
    {
        var snapshot = Snapshot(Members(5));
        SyntheticCohortSnapshot[] invalid =
        [
            snapshot with { Provenance = (DataProvenance)999 }, snapshot with { Provenance = DataProvenance.Real },
            snapshot with { CohortRevision = 0 }, snapshot with { CatalogRevision = 0 },
            snapshot with { EnforcementRevision = 0 }, snapshot with { CohortId = " " },
        ];
        foreach (var input in invalid) Assert.Null(WholeCohortCandidates.ReviewControlledSynthetic(input, [Visitor], Supported));
        Assert.Null(WholeCohortCandidates.ReviewControlledSynthetic(snapshot, [Visitor], Supported with { ExternalLinkage = (CandidateRiskVerdict)999 }));
    }

    private static decimal D(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    private static CandidateMeasurement Measured(decimal value) => new(CandidateMeasurementState.Measured, value);
    private static ImmutableArray<SyntheticCohortMember> Members(int count) => Enumerable.Range(1, count)
        .Select(id => new SyntheticCohortMember(id, null, Measured(90.1m), Measured(1200m), [], 1, 1)).ToImmutableArray();
    private static SyntheticCohortSnapshot Snapshot(ImmutableArray<SyntheticCohortMember> members) => new("synthetic-field", 1, DataProvenance.Demo, 1, 1, true, members);
    private static WholeCohortCandidateArtifact? Candidate(SyntheticCohortSnapshot snapshot) => Prepare(snapshot,
        WholeCohortCandidates.ReviewControlledSynthetic(snapshot, [Visitor], Supported), Visitor);
    private static WholeCohortCandidateArtifact? Prepare(SyntheticCohortSnapshot snapshot, ControlledCandidateReview? review, CandidateVariant variant) =>
        WholeCohortCandidates.Prepare(WholeCohortCandidates.CatalogId, WholeCohortCandidates.Scope, snapshot, review, variant);
}
