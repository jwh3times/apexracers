using ApexRacers.Core;
using Xunit;

namespace ApexRacers.Tests.Models;

/// <summary>
/// "Which week is the season in?" was answered two different ways — the series list took the latest
/// start date, the standings page took the highest week number — and nothing tested either rule
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

        Assert.Equal(1, SeasonCalendar.CurrentWeekNumber(weeks, Today));
    }

    [Fact]
    public void IncludesAWeekStartingExactlyToday()
    {
        var weeks = new[] { Week(0, "2026-03-08"), Week(1, "2026-03-15") };

        Assert.Equal(1, SeasonCalendar.CurrentWeekNumber(weeks, Today));
    }

    [Fact]
    public void IsNullBeforeTheSeasonStarts()
    {
        // Deliberately not a fallback: the two callers want different ones, so the choice is theirs.
        var weeks = new[] { Week(0, "2026-04-01"), Week(1, "2026-04-08") };

        Assert.Null(SeasonCalendar.CurrentWeekNumber(weeks, Today));
    }

    [Fact]
    public void IsNullForAnEmptySeason()
    {
        Assert.Null(SeasonCalendar.CurrentWeekNumber([], Today));
    }

    [Fact]
    public void IgnoresTheOrderWeeksArriveIn()
    {
        var weeks = new[] { Week(2, "2026-03-22"), Week(0, "2026-03-01"), Week(1, "2026-03-08") };

        Assert.Equal(1, SeasonCalendar.CurrentWeekNumber(weeks, Today));
    }

    [Fact]
    public void PrefersTheLaterStartDateOverTheHigherWeekNumber()
    {
        // The case the two old rules disagreed on. Week 5 is numbered higher, but week 9 started
        // more recently — the old standings rule answered 9's number only by coincidence of
        // ordering, and the old series-list rule answered by date. Date wins.
        var weeks = new[] { Week(9, "2026-03-01"), Week(5, "2026-03-10") };

        Assert.Equal(5, SeasonCalendar.CurrentWeekNumber(weeks, Today));
    }

    [Fact]
    public void BreaksAStartDateTieOnTheHigherWeekNumber()
    {
        // Duplicated start dates are the other way the two rules could have diverged; picking the
        // higher number keeps the answer defined rather than dependent on row order.
        var weeks = new[] { Week(3, "2026-03-08"), Week(4, "2026-03-08") };

        Assert.Equal(4, SeasonCalendar.CurrentWeekNumber(weeks, Today));
    }

    [Fact]
    public void HandlesAWeekNumberedZero()
    {
        // iRacing weeks are zero-based, so week 0 must not be mistaken for "no week".
        var weeks = new[] { Week(0, "2026-03-01"), Week(1, "2026-03-22") };

        Assert.Equal(0, SeasonCalendar.CurrentWeekNumber(weeks, Today));
    }
}
