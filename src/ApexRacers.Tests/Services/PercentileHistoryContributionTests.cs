using ApexRacers.Core.PercentileHistory;
using System.Collections.Immutable;
using ApexRacers.Api.Services.PercentileHistory;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using Xunit;

namespace ApexRacers.Tests.Services;

/// <summary>Pure snapshot accounting, not freshness, persistence or publication acceptance.</summary>
public sealed class PercentileHistoryContributionTests
{
    private static readonly PercentileHistoryContext Context = new(
        new(Guid.NewGuid(), 100001, DataProvenance.Demo, PercentileOwnerKind.DemoSubject), null, null,
        PercentileSelection.FromRequest(false, null), 1, 2);
    private static readonly PercentileHistoryWeek Requested = new(Guid.NewGuid(), 20, new(2026, 10, 14), 101);
    private static readonly PercentileHistoryPreparation Preparation = new(Guid.NewGuid(), 1, 1);
    private static readonly ImmutableArray<PercentileHistorySource> Sources = [new(Guid.NewGuid(), 1)];

    [Fact]
    public void ChronologicalCutoffIncludesEarlierSeasonsAndSameStartDateButNeverLaterWeek()
    {
        var earlier = Reading(50, Requested with { Id = Guid.NewGuid(), SeasonId = 10, StartDate = Requested.StartDate.AddDays(-7) });
        var current = Reading(30);
        var later = Reading(90, Requested with { Id = Guid.NewGuid(), StartDate = Requested.StartDate.AddDays(7) });
        var history = Build(earlier, current, later);
        Assert.Equal(40, history.ExpectedPercentile);
        Assert.Equal(2, history.Contributions.Length);
        Assert.Equal(earlier, history.Contributions[0]);
        Assert.Equal(50, Build(earlier, current, Reading(70, Requested with { Id = Guid.NewGuid() })).ExpectedPercentile);
    }

    [Fact]
    public void OneWeekIdentityContributesOnlyOnceAndDoesNotUseSeasonWeekNumbers()
    {
        var reading = Reading(30);
        var duplicate = reading with { Sources = [.. Sources] };
        var result = Build(reading, duplicate);
        Assert.Single(result.Contributions);
        Assert.Equal(30, result.ExpectedPercentile);
    }

    [Fact]
    public void SourceSetsAreOrderIndependentButVersionsMustAgree()
    {
        var first = new PercentileHistorySource(Guid.NewGuid(), 1);
        var second = new PercentileHistorySource(Guid.NewGuid(), 2);
        var reading = Reading(30) with { Sources = [first, second] };
        Assert.Single(Build(reading, reading with { Sources = [second, first] }).Contributions);
        Assert.Throws<InvalidOperationException>(() => Build(reading, reading with { Sources = [first, second with { Version = 3 }] }));
    }

    [Fact]
    public void ExplicitlyEmptySourcesCanRepresentPreparedEvidenceAbsence()
    {
        var missing = Reading(30) with { PersonalBest = null, PercentileRank = null, TopSharePercent = null, FieldSize = 0, Sources = [] };
        Assert.Null(Build(missing).ExpectedPercentile);
        Assert.Empty(Build(missing).Contributions);
        Assert.Throws<ArgumentException>(() => Build(missing with { Sources = default }));
    }

    [Fact]
    public void EvidenceAndWinningSessionTypeMustMatchCanonicalSelection()
    {
        var uploaded = Reading(90) with { PersonalBest = new(90, LapEvidence.UploadedLap), UploadedSessionType = LapSessionType.Practice };
        Assert.Throws<ArgumentException>(() => Build(uploaded));
        Assert.Throws<ArgumentException>(() => Build(Reading(30) with { PersonalBest = new(110, (LapEvidence)255) }));
        Assert.Throws<ArgumentException>(() => Build(Reading(30) with { UploadedSessionType = LapSessionType.Race }));
        var restricted = Context with { Selection = PercentileSelection.FromRequest(true, [LapSessionType.Race]) };
        uploaded = uploaded with { Context = restricted };
        Assert.Throws<ArgumentException>(() => PercentileHistoryContributions.Build(restricted, Requested, [uploaded]));
        Assert.Throws<ArgumentException>(() => PercentileHistoryContributions.Build(restricted, Requested,
            [uploaded with { UploadedSessionType = null }]));
        var valid = uploaded with { UploadedSessionType = LapSessionType.Unknown };
        Assert.Single(PercentileHistoryContributions.Build(restricted, Requested, [valid]).Contributions);
    }

    [Fact]
    public void UnclassifiedOwnerRevisionContextIsRejectedEvenWhenHistoryIsEmpty()
    {
        Assert.Throws<ArgumentException>(() => PercentileHistoryContributions.Build(Context with { OwnerRevision = 1 }, Requested, []));
        var personal = Context with { Owner = Context.Owner with { Kind = PercentileOwnerKind.Personal }, GrantId = Guid.NewGuid(), OwnerRevision = 1 };
        Assert.Null(PercentileHistoryContributions.Build(personal, Requested, []).ExpectedPercentile);
        Assert.Throws<ArgumentException>(() => PercentileHistoryContributions.Build(personal with { GrantId = null }, Requested, []));
    }

    [Fact]
    public void ReplacementAndRemovalUseOnlyCurrentSnapshotsNotOldContributions()
    {
        var earlier = Reading(50, Requested with { Id = Guid.NewGuid(), StartDate = Requested.StartDate.AddDays(-7) });
        Assert.Equal(40, Build(earlier, Reading(30)).ExpectedPercentile);
        Assert.Equal(70, Build(earlier, Reading(90) with { ReadingRevision = 2 }).ExpectedPercentile);
        var missing = Reading(90) with { PersonalBest = null, PercentileRank = null, TopSharePercent = null, ReadingRevision = 3 };
        Assert.Equal(50, Build(earlier, missing).ExpectedPercentile);
        Assert.Equal(50, Build(earlier, Reading(90) with { FieldSize = 4 }).ExpectedPercentile);
        Assert.Null(Build(missing).ExpectedPercentile);
        Assert.Null(Build().ExpectedPercentile);
        Assert.Empty(Build(Reading(90) with { FieldSize = 4 }).Contributions);
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    public void HeadlineEligibilityUsesSharedMinimumFieldSize(int size, bool eligible)
    {
        Assert.Equal(eligible ? 1 : 0, Build(Reading(30) with { FieldSize = size }).Contributions.Length);
    }

    [Fact]
    public void ConflictingWeekRevisionsOrTrackIdentityAreNeverArbitrarilySelected()
    {
        var reading = Reading(30);
        Assert.Throws<InvalidOperationException>(() => Build(reading, reading with { PercentileRank = 90, ReadingRevision = 2 }));
        Assert.Throws<InvalidOperationException>(() => Build(reading with { Week = Requested with { TrackId = 102 } }));
        Assert.Throws<InvalidOperationException>(() => Build(reading, reading with { Preparation = Preparation with { Version = 2 } }));
        Assert.Throws<InvalidOperationException>(() => Build(reading, reading with { Sources = [new(Sources[0].CopyId, 2)] }));
    }

    [Fact]
    public void OwnerSelectionSeriesCarAndGrantRevisionsCannotBeMixed()
    {
        var contexts = new[] { Context with { Owner = Context.Owner with { UserId = Guid.NewGuid() } },
            Context with { Owner = Context.Owner with { CustomerId = 42 } },
            Context with { Owner = Context.Owner with { Provenance = DataProvenance.Real } },
            Context with { Selection = PercentileSelection.FromRequest(true, null) },
            Context with { SeriesId = 3 }, Context with { CarId = 4 }, Context with { OwnerRevision = 2 } };
        foreach (var context in contexts)
            Assert.Throws<InvalidOperationException>(() => Build(Reading(30) with { Context = context }));
    }

    [Fact]
    public void ReadingKeepsItsOwnPersonalBestSelectionAndSourcePreparationContext()
    {
        var reading = Reading(30);
        var result = Build(reading);
        Assert.Equal(reading, Assert.Single(result.Contributions));
        Assert.Equal(Context.Selection, result.Context.Selection);
        Assert.Equal(new PersonalBest(110, LapEvidence.RaceLap), result.Contributions[0].PersonalBest);
        Assert.Equal(Preparation, result.Contributions[0].Preparation);
        Assert.Equal(Sources, result.Contributions[0].Sources);
    }

    [Fact]
    public void InitialPurposeVersionZeroIsARealPreparationVersionButNegativeIsNot()
    {
        var personal = Context with { Owner = Context.Owner with { Kind = PercentileOwnerKind.Personal }, GrantId = Guid.NewGuid(), OwnerRevision = 1 };
        var reading = Reading(30) with { Context = personal, Preparation = Preparation with { Version = 0 } };
        Assert.Equal(30, PercentileHistoryContributions.Build(personal, Requested, [reading]).ExpectedPercentile);
        Assert.Throws<ArgumentException>(() => PercentileHistoryContributions.Build(personal, Requested,
            [reading with { Preparation = Preparation with { Version = -1 } }]));
    }

    [Fact]
    public void UnclassifiedOrInvalidMetadataAndMetricsAreRejectedRatherThanReused()
    {
        var reading = Reading(30);
        Assert.Throws<ArgumentException>(() => Build(reading with { Sources = [] }));
        Assert.Throws<ArgumentException>(() => Build(reading with { ReadingRevision = 0 }));
        Assert.Throws<ArgumentException>(() => Build(reading with { Preparation = Preparation with { Generation = 0 } }));
        Assert.Throws<ArgumentException>(() => Build(reading with { Sources = [Sources[0], Sources[0]] }));
        Assert.Throws<ArgumentException>(() => Build(reading with { PercentileRank = double.NaN }));
        Assert.Throws<ArgumentException>(() => Build(reading with { PercentileRank = null }));
        Assert.Throws<ArgumentException>(() => Build(reading with { PersonalBest = new(0, LapEvidence.RaceLap) }));
    }

    private static PercentileHistoryReading Reading(double rank, PercentileHistoryWeek? week = null) =>
        new(Context, week ?? Requested, new(110, LapEvidence.RaceLap), 5, rank, 60, 1, Preparation, Sources);
    private static PercentileHistoryResult Build(params PercentileHistoryReading[] readings) =>
        PercentileHistoryContributions.Build(Context, Requested, readings);
}
