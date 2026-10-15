using ApexRacers.Core.PercentileHistory;
using System.Collections.Immutable;
using ApexRacers.Core;
using ApexRacers.Core.Models;

namespace ApexRacers.Api.Services.PercentileHistory;

/// <summary>Pure accounting over classified snapshots only. Freshness, authority, source retention,
/// replacement/removal persistence and protected dispatch belong to the later integrated refresh.</summary>
public static class PercentileHistoryContributions
{
    public static PercentileHistoryResult Build(PercentileHistoryContext context,
        PercentileHistoryWeek requestedWeek, IEnumerable<PercentileHistoryReading> readings)
    {
        ValidateContext(context);
        if (requestedWeek.Id == Guid.Empty) throw new ArgumentException("A requested Week identity is required.");
        var weeks = new Dictionary<Guid, PercentileHistoryReading>();
        foreach (var reading in readings)
        {
            if (reading.Context != context) throw new InvalidOperationException("Conflicting percentile history context.");
            Validate(reading);
            if (reading.Week.Id == requestedWeek.Id && reading.Week != requestedWeek)
                throw new InvalidOperationException("Conflicting requested Week identity.");
            if (weeks.TryGetValue(reading.Week.Id, out var prior))
            {
                // Even a newer-looking revision cannot be chosen without the storage CAS boundary.
                if (prior != reading && !SameReading(prior, reading))
                    throw new InvalidOperationException("Conflicting percentile history Week revisions.");
            }
            else weeks.Add(reading.Week.Id, reading);
        }
        var contributions = weeks.Values.Where(r => r.Week.StartDate <= requestedWeek.StartDate
                && r.PersonalBest is not null && FieldPercentile.IsPresentable(r.FieldSize))
            .OrderBy(r => r.Week.StartDate).ThenBy(r => r.Week.Id).ToImmutableArray();
        return new(context, requestedWeek, contributions,
            contributions.Length == 0 ? null : contributions.Average(r => r.PercentileRank!.Value));
    }

    private static bool SameReading(PercentileHistoryReading left, PercentileHistoryReading right) =>
        left with { Sources = right.Sources } == right
        && left.Sources.OrderBy(s => s.CopyId).SequenceEqual(right.Sources.OrderBy(s => s.CopyId));

    private static void ValidateContext(PercentileHistoryContext context)
    {
        if (context.Owner.UserId == Guid.Empty || context.Owner.CustomerId <= 0
            || context.Owner.Provenance is not (DataProvenance.Real or DataProvenance.Demo)
            || context.CarId <= 0 || context.SeriesId <= 0 || context.Selection is null
            || (context.Owner.Kind switch
            {
                PercentileOwnerKind.DemoSubject => context.Owner.Provenance != DataProvenance.Demo
                    || context.Owner.CustomerId != DemoData.DriverCustId || context.GrantId is not null || context.OwnerRevision is not null,
                PercentileOwnerKind.Personal => context.GrantId is null || context.GrantId == Guid.Empty || context.OwnerRevision is not > 0,
                _ => true,
            }))
            throw new ArgumentException("A history context requires a classified owner, selection and revision.");
    }

    private static void Validate(PercentileHistoryReading reading)
    {
        if (reading.Week.Id == Guid.Empty || reading.FieldSize < 0 || reading.ReadingRevision <= 0
            || reading.Preparation.PurposeId == Guid.Empty || reading.Preparation.Generation <= 0 || reading.Preparation.Version < 0
            || reading.Sources.IsDefault || (reading.PersonalBest is not null && reading.Sources.Length == 0)
            || reading.Sources.Any(s => s.CopyId == Guid.Empty || s.Version <= 0)
            || reading.Sources.Select(s => s.CopyId).Distinct().Count() != reading.Sources.Length)
            throw new ArgumentException("A classified history reading requires consistent source and revision metadata.");
        if (reading.PersonalBest is null && (reading.PercentileRank is not null || reading.TopSharePercent is not null || reading.UploadedSessionType is not null))
            throw new ArgumentException("A missing Personal Best cannot carry a ranked lap or upload context.");
        if (reading.PersonalBest is { } evidenceBest && (evidenceBest.Evidence switch
            {
                LapEvidence.RaceLap => reading.UploadedSessionType is not null,
                LapEvidence.UploadedLap => reading.UploadedSessionType is not { } type || !reading.Context.Selection.Includes(type),
                _ => true,
            }))
            throw new ArgumentException("The Personal Best evidence must match its canonical selection and session type.");
        if (reading.PersonalBest is { } best && (!double.IsFinite(best.LapSeconds) || best.LapSeconds <= 0
            || reading.FieldSize == 0 || reading.PercentileRank is not { } rank || !double.IsFinite(rank) || rank is <= 0 or >= 100
            || reading.TopSharePercent is not (> 0 and <= 100)))
            throw new ArgumentException("A Personal Best reading requires valid Field metrics.");
    }
}
