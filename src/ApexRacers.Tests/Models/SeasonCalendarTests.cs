using ApexRacers.Core;
using Xunit;

namespace ApexRacers.Tests.Models;

/// <summary>
/// "Which week is the season in?" was answered two different ways — the series list took the latest
/// start date, the standings page took the highest Race Week Index — and nothing tested either rule
/// directly. The standings tests reached it only through a three-week fixture; the series tests only
/// through a full query.
/// </summary>
public class SeasonCalendarTests
{
    private static readonly DateOnly Today = new(2026, 3, 15);

    private static (int, DateOnly) Week(int number, string start) =>
        (number, DateOnly.Parse(start));

    [Fact]
    public void PicksTheLatestWeekThatHasAlreadyStarted()
    {
        var weeks = new[]
        {
            Week(0, "2026-03-01"),
            Week(1, "2026-03-08"),
            Week(2, "2026-03-22"), // still in the future
        };

        Assert.Equal(1, SeasonCalendar.CurrentRaceWeekIndex(weeks, Today));
    }

    [Fact]
    public void IncludesAWeekStartingExactlyToday()
    {
        var weeks = new[] { Week(0, "2026-03-08"), Week(1, "2026-03-15") };

        Assert.Equal(1, SeasonCalendar.CurrentRaceWeekIndex(weeks, Today));
    }

    [Fact]
    public void IsNullBeforeTheSeasonStarts()
    {
        // Deliberately not a fallback: the two callers want different ones, so the choice is theirs.
        var weeks = new[] { Week(0, "2026-04-01"), Week(1, "2026-04-08") };

        Assert.Null(SeasonCalendar.CurrentRaceWeekIndex(weeks, Today));
    }

    [Fact]
    public void IsNullForAnEmptySeason()
    {
        Assert.Null(SeasonCalendar.CurrentRaceWeekIndex([], Today));
    }

    [Fact]
    public void IgnoresTheOrderWeeksArriveIn()
    {
        var weeks = new[] { Week(2, "2026-03-22"), Week(0, "2026-03-01"), Week(1, "2026-03-08") };

        Assert.Equal(1, SeasonCalendar.CurrentRaceWeekIndex(weeks, Today));
    }

    [Fact]
    public void PrefersTheLaterStartDateOverTheHigherRaceWeekIndex()
    {
        // The case the two old rules disagreed on. Week 5 is numbered higher, but week 9 started
        // more recently — the old standings rule answered 9's number only by coincidence of
        // ordering, and the old series-list rule answered by date. Date wins.
        var weeks = new[] { Week(9, "2026-03-01"), Week(5, "2026-03-10") };

        Assert.Equal(5, SeasonCalendar.CurrentRaceWeekIndex(weeks, Today));
    }

    [Fact]
    public void BreaksAStartDateTieOnTheHigherRaceWeekIndex()
    {
        // Duplicated start dates are the other way the two rules could have diverged; picking the
        // higher number keeps the answer defined rather than dependent on row order.
        var weeks = new[] { Week(3, "2026-03-08"), Week(4, "2026-03-08") };

        Assert.Equal(4, SeasonCalendar.CurrentRaceWeekIndex(weeks, Today));
    }

    [Fact]
    public void HandlesARaceWeekIndexedZero()
    {
        // iRacing weeks are zero-based, so week 0 must not be mistaken for "no week".
        var weeks = new[] { Week(0, "2026-03-01"), Week(1, "2026-03-22") };

        Assert.Equal(0, SeasonCalendar.CurrentRaceWeekIndex(weeks, Today));
    }
}

/// <summary>
/// The changeover rule. Ordering active seasons by year-and-quarter picked the upcoming season the
/// moment iRacing flagged it — before a single race week had begun — so schedule, standings,
/// strategy and week data could all read an empty season while the current quarter was still
/// running. These pin the boundary that replaced it.
/// </summary>
public class SeasonCalendarCurrentSeasonTests
{
    private const int Q2 = 5001;
    private const int Q3 = 5002;

    /// <summary>Q2 began in March; Q3 is flagged active but does not begin until 1 June.</summary>
    private static SeasonStart[] Changeover() =>
    [
        new(Q2, new DateOnly(2026, 3, 10), 2026, 2, Active: true),
        new(Q3, new DateOnly(2026, 6, 1), 2026, 3, Active: true),
    ];

    [Theory]
    [InlineData("2026-05-31", Q2)] // the day before  — Q2 still current
    [InlineData("2026-06-01", Q3)] // the day itself  — Q3 takes over
    [InlineData("2026-06-02", Q3)] // the day after   — and stays
    public void SwitchesSeasonsOnTheLaterSeasonsFirstRaceWeekStartDate(string today, int expected)
    {
        Assert.Equal(expected, SeasonCalendar.CurrentSeasonId(Changeover(), DateOnly.Parse(today)));
    }

    [Fact]
    public void KeepsThePriorSeasonThroughTheGapAfterItsFinalWeek()
    {
        // Weeks are irrelevant here beyond the first: the season holds the slot on its *start*
        // date until a successor starts, so the gap after its last race is covered by the same rule.
        Assert.Equal(Q2, SeasonCalendar.CurrentSeasonId(Changeover(), new DateOnly(2026, 5, 20)));
    }

    [Fact]
    public void KeepsThePriorSeasonEvenAfterUpstreamClearsItsActiveFlag()
    {
        // The changeover case that motivated the rule: iRacing deactivates the outgoing season
        // before the incoming one starts. Active is a status, not the selection rule.
        SeasonStart[] seasons =
        [
            new(Q2, new DateOnly(2026, 3, 10), 2026, 2, Active: false),
            new(Q3, new DateOnly(2026, 6, 1), 2026, 3, Active: true),
        ];

        Assert.Equal(Q2, SeasonCalendar.CurrentSeasonId(seasons, new DateOnly(2026, 5, 31)));
    }

    [Fact]
    public void IgnoresANewerQuarterThatHasNotBegun()
    {
        // Year and quarter are tiebreakers only. A Q4 season stored ahead of time must not
        // outrank the quarter actually being raced.
        SeasonStart[] seasons =
        [
            new(Q2, new DateOnly(2026, 3, 10), 2026, 2, Active: true),
            new(9999, new DateOnly(2026, 12, 1), 2026, 4, Active: true),
        ];

        Assert.Equal(Q2, SeasonCalendar.CurrentSeasonId(seasons, new DateOnly(2026, 5, 31)));
    }

    [Fact]
    public void FallsBackToTheNewestActiveSeasonWhenNoneHasBegun()
    {
        // A series with only an upcoming season keeps its pre-existing behaviour: there is no
        // "began most recently" to choose, so the newest active season stands in.
        SeasonStart[] seasons =
        [
            new(Q2, new DateOnly(2026, 7, 1), 2026, 2, Active: true),
            new(Q3, new DateOnly(2026, 9, 1), 2026, 3, Active: true),
        ];

        Assert.Equal(Q3, SeasonCalendar.CurrentSeasonId(seasons, new DateOnly(2026, 6, 1)));
    }

    [Fact]
    public void TreatsASeasonWithNoStoredWeeksAsNotBegun()
    {
        SeasonStart[] seasons =
        [
            new(Q2, new DateOnly(2026, 3, 10), 2026, 2, Active: false),
            new(Q3, FirstRaceWeekStart: null, 2026, 3, Active: true),
        ];

        Assert.Equal(Q2, SeasonCalendar.CurrentSeasonId(seasons, new DateOnly(2026, 5, 31)));
    }

    [Fact]
    public void IsNullWhenNoSeasonHasBegunAndNoneIsActive()
    {
        SeasonStart[] seasons = [new(Q2, new DateOnly(2026, 7, 1), 2026, 2, Active: false)];

        Assert.Null(SeasonCalendar.CurrentSeasonId(seasons, new DateOnly(2026, 6, 1)));
    }

    [Fact]
    public void IsNullForASeriesWithNoSeasons()
    {
        Assert.Null(SeasonCalendar.CurrentSeasonId([], new DateOnly(2026, 6, 1)));
    }

    [Fact]
    public void BreaksAFirstWeekDateTieDeterministically()
    {
        // Two seasons sharing a first-week date is not expected, but leaving the answer to
        // enumeration order would make it flap between requests.
        SeasonStart[] seasons =
        [
            new(Q2, new DateOnly(2026, 3, 10), 2026, 2, Active: true),
            new(Q3, new DateOnly(2026, 3, 10), 2026, 3, Active: true),
        ];

        Assert.Equal(Q3, SeasonCalendar.CurrentSeasonId(seasons, new DateOnly(2026, 5, 31)));
        Assert.Equal(Q3, SeasonCalendar.CurrentSeasonId(seasons.Reverse(), new DateOnly(2026, 5, 31)));
    }
}
