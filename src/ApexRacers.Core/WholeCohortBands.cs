using System.Collections.Immutable;

namespace ApexRacers.Core;

public enum CandidateAudience { Visitor, SignedInPreview, SignedInOther }
public enum CandidateMeasurementState { Measured, Missing, Unsupported }
public sealed record CandidateMeasurement(CandidateMeasurementState State, decimal Value = 0);
public sealed record CandidateVariant(CandidateAudience Audience, int? OwnerCustomerId = null,
    decimal? OwnerPersonalBestLapSeconds = null);
public sealed record SyntheticCohortMember(int CustomerId, string? DriverName,
    CandidateMeasurement LapSeconds, CandidateMeasurement AbsoluteRating,
    ImmutableArray<CandidateAudience> SharingAudiences, long AuthorityRevision, long EvidenceRevision);
public sealed record CandidateRange(decimal LowerInclusive, decimal UpperExclusive);
public sealed record HiddenBandGroup(CandidateRange? LapSeconds, CandidateRange? AbsoluteRating);
public sealed record ConsentedBandRow(string DriverName, CandidateRange? LapSeconds, CandidateRange? AbsoluteRating);
public sealed record CandidateOwnerAnalytics(double PercentileRank);
public sealed record WholeCohortProjection(int Level, ImmutableArray<HiddenBandGroup> HiddenGroups,
    ImmutableArray<ConsentedBandRow> NamedRows, CandidateOwnerAnalytics? OwnerAnalytics);

/// <summary>Pure normalized synthetic candidate math. This does not authorize or dispatch a release.</summary>
public static class WholeCohortBands
{
    public const int MinimumHiddenDrivers = 5;

    public static CandidateRange Range(decimal value, decimal width)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        // Division can round an exact decimal just below a boundary onto that boundary.
        // Remainder retains the fractional displacement; adjust negative truncation to floor.
        var remainder = value % width;
        var lower = value - remainder;
        if (remainder < 0) lower -= width;
        var upper = lower + width;
        if (upper - lower != width) throw new OverflowException("Band boundaries are not representable.");
        return new(lower, upper);
    }

    public static CandidateRange LapRange(decimal value, int level) => Range(value, Width(level) / 1000m);

    public static CandidateRange RatingRange(decimal value, int level) => Range(value,
        level == 0 ? value is >= 1000m and < 2500m ? 250m : 500m : Width(level));

    private static decimal Width(int level) => level switch
    {
        0 => 500m,
        1 => 1000m,
        2 => 2000m,
        3 => 4000m,
        4 => 8000m,
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };

    public static WholeCohortProjection? Prepare(ImmutableArray<SyntheticCohortMember> cohort,
        ImmutableArray<CandidateVariant> reviewedVariants, CandidateVariant requestedVariant)
    {
        if (!InputsValid(cohort, reviewedVariants) || !reviewedVariants.Contains(requestedVariant)) return null;
        var members = cohort.DistinctBy(m => m.CustomerId).ToArray();

        // One selection covers every explicitly reviewed owner/audience variant. Paging or a
        // requested owner cannot choose a different finer partition of this complete cohort.
        for (var level = 0; level <= 4; level++)
        {
            if (!reviewedVariants.All(v => Hidden(members, v).GroupBy(m => Key(m, level))
                .All(g => g.Count() >= MinimumHiddenDrivers))) continue;
            var hidden = Hidden(members, requestedVariant).Select(m => Key(m, level))
                .Where(g => g.LapSeconds is not null || g.AbsoluteRating is not null).Distinct()
                .OrderBy(g => g.LapSeconds?.LowerInclusive).ThenBy(g => g.AbsoluteRating?.LowerInclusive).ToImmutableArray();
            var named = members.Where(m => m.CustomerId != requestedVariant.OwnerCustomerId && Disclosed(m, requestedVariant))
                .Select(m => new ConsentedBandRow(m.DriverName!, Key(m, level).LapSeconds, Key(m, level).AbsoluteRating))
                .OrderBy(m => m.DriverName, StringComparer.Ordinal).ToImmutableArray();
            var owner = members.SingleOrDefault(m => m.CustomerId == requestedVariant.OwnerCustomerId);
            CandidateOwnerAnalytics? analytics = owner is not null
                && (requestedVariant.OwnerPersonalBestLapSeconds is not null || owner.LapSeconds.State == CandidateMeasurementState.Measured)
                ? new(FieldPercentile.Rank((double)(requestedVariant.OwnerPersonalBestLapSeconds ?? owner.LapSeconds.Value),
                    members.Where(m => m.CustomerId != owner.CustomerId && m.LapSeconds.State == CandidateMeasurementState.Measured)
                        .Select(m => (double)m.LapSeconds.Value).ToArray())) : null;
            return hidden.IsEmpty && named.IsEmpty && analytics is null ? null : new(level, hidden, named, analytics);
        }
        return null;
    }

    public static bool InputsValid(ImmutableArray<SyntheticCohortMember> cohort, ImmutableArray<CandidateVariant> reviewedVariants)
    {
        if (cohort.IsDefaultOrEmpty || reviewedVariants.IsDefaultOrEmpty) return false;
        if (cohort.Any(m => m is null || m.CustomerId <= 0 || m.AuthorityRevision <= 0 || m.EvidenceRevision <= 0
            || m.SharingAudiences.IsDefault || !Valid(m.LapSeconds) || !Valid(m.AbsoluteRating)
            || m.SharingAudiences.Any(a => !Enum.IsDefined(a) || a == CandidateAudience.Visitor)
            || (!m.SharingAudiences.IsEmpty && string.IsNullOrWhiteSpace(m.DriverName)))) return false;

        // A normalized Field contains one best measurement per Driver. Identical repeated rows
        // never inflate support; contradictory rows are not silently reduced by an invented rule.
        var groups = cohort.GroupBy(m => m.CustomerId).ToArray();
        if (groups.Any(g => g.Skip(1).Any(m => !Same(g.First(), m)))) return false;
        var members = groups.Select(g => g.First()).ToArray();
        if (reviewedVariants.Any(v => v is null || !Enum.IsDefined(v.Audience)
            || v.OwnerPersonalBestLapSeconds is not null && v.OwnerCustomerId is null
            || v.OwnerCustomerId is { } id && (v.Audience == CandidateAudience.Visitor || !members.Any(m => m.CustomerId == id))))
            return false;
        return true;
    }

    private static bool Valid(CandidateMeasurement measurement) => measurement is not null && Enum.IsDefined(measurement.State);
    private static bool Same(SyntheticCohortMember left, SyntheticCohortMember right) =>
        left with { SharingAudiences = right.SharingAudiences } == right
        && left.SharingAudiences.Order().SequenceEqual(right.SharingAudiences.Order());
    private static bool Disclosed(SyntheticCohortMember m, CandidateVariant v) =>
        v.Audience != CandidateAudience.Visitor && m.SharingAudiences.Contains(v.Audience);
    private static IEnumerable<SyntheticCohortMember> Hidden(IEnumerable<SyntheticCohortMember> members, CandidateVariant variant) =>
        members.Where(m => m.CustomerId != variant.OwnerCustomerId && !Disclosed(m, variant));
    private static HiddenBandGroup Key(SyntheticCohortMember m, int level) => new(
        m.LapSeconds.State == CandidateMeasurementState.Measured ? LapRange(m.LapSeconds.Value, level) : null,
        m.AbsoluteRating.State == CandidateMeasurementState.Measured ? RatingRange(m.AbsoluteRating.Value, level) : null);
}
