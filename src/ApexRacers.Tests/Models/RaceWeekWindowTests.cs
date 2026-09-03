using ApexRacers.Core;
using Xunit;

namespace ApexRacers.Tests.Models;

/// <summary>
/// The span a Race Week covers decides which Uploaded Laps may be ranked against its Field, so the
/// boundary rules are pinned here rather than being inferred from a service test's fixture dates.
/// </summary>
public class RaceWeekWindowTests
{
    private static readonly DateOnly Start = new(2026, 6, 15);

    // ── Which end is used ─────────────────────────────────────────────────────

    [Fact]
    public void For_ReportedEndTime_IsUsedVerbatimAndMarkedExact()
    {
        // iRacing closes a week at 21:00 UTC on the seventh day, not at midnight on the eighth.
        var reported = new DateTimeOffset(2026, 6, 21, 21, 0, 0, TimeSpan.Zero);

        var window = RaceWeekWindow.For(Start, reported);

        Assert.Equal(new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero), window.Start);
        Assert.Equal(reported, window.End);
        Assert.True(window.IsExact);
    }

    [Fact]
    public void For_NoReportedEnd_FallsBackToTheNextWeekStart()
    {
        var window = RaceWeekWindow.For(Start, reportedEnd: null, nextWeekStart: new DateOnly(2026, 6, 22));

        Assert.Equal(new DateTimeOffset(2026, 6, 22, 0, 0, 0, TimeSpan.Zero), window.End);
        Assert.False(window.IsExact);
    }

    [Fact]
    public void For_NoReportedEndAndNoSuccessor_FallsBackToSevenDays()
    {
        // A season's final week has no successor to be bounded by.
        var window = RaceWeekWindow.For(Start);

        Assert.Equal(new DateTimeOffset(2026, 6, 22, 0, 0, 0, TimeSpan.Zero), window.End);
        Assert.False(window.IsExact);
    }

    [Fact]
    public void For_DefaultValuedEndTime_IsIgnoredRatherThanTrusted()
    {
        // The SDK types week_end_time as non-nullable, so an omitted field deserializes to
        // 0001-01-01 — a successful parse carrying a value that is not a date. Treating it as the
        // end would make the week close two millennia before it opened, admitting nothing at all.
        var window = RaceWeekWindow.For(Start, reportedEnd: default(DateTimeOffset));

        Assert.Equal(new DateTimeOffset(2026, 6, 22, 0, 0, 0, TimeSpan.Zero), window.End);
        Assert.False(window.IsExact);
    }

    [Fact]
    public void For_ReportedEndBeforeTheStart_IsIgnored()
    {
        var window = RaceWeekWindow.For(Start, new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(2026, 6, 22, 0, 0, 0, TimeSpan.Zero), window.End);
        Assert.False(window.IsExact);
    }

    // ── Containment ───────────────────────────────────────────────────────────

    [Fact]
    public void Contains_TheStartInstant_IsInside()
    {
        var window = RaceWeekWindow.For(Start);

        Assert.True(window.Contains(new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Contains_TheEndInstant_IsOutside()
    {
        // The end is exclusive so two contiguous Weeks cannot both claim one Lap.
        var window = RaceWeekWindow.For(Start);

        Assert.False(window.Contains(new DateTimeOffset(2026, 6, 22, 0, 0, 0, TimeSpan.Zero)));
    }

    [Theory]
    [InlineData("2026-06-14T23:59:59Z", false)] // a minute before the week opened
    [InlineData("2026-06-17T12:00:00Z", true)]  // mid-week
    [InlineData("2026-06-21T20:59:59Z", true)]  // a second before iRacing closes it
    [InlineData("2026-06-21T21:00:00Z", false)] // the closing instant itself
    [InlineData("2026-06-21T22:00:00Z", false)] // the hour that start + 7 days would have admitted
    public void Contains_AgainstAReportedEnd_MatchesTheReportedBoundary(string instant, bool expected)
    {
        var window = RaceWeekWindow.For(Start, new DateTimeOffset(2026, 6, 21, 21, 0, 0, TimeSpan.Zero));

        Assert.Equal(expected, window.Contains(DateTimeOffset.Parse(instant)));
    }

    [Fact]
    public void Contains_ComparesInstantsNotWallClocks()
    {
        // 2026-06-14 20:00 -05:00 is 2026-06-15 01:00 UTC, which is inside the week even though
        // its local date reads as the day before.
        var window = RaceWeekWindow.For(Start);

        Assert.True(window.Contains(new DateTimeOffset(2026, 6, 14, 20, 0, 0, TimeSpan.FromHours(-5))));
    }

    // ── A whole season ────────────────────────────────────────────────────────

    [Fact]
    public void ForSeason_BoundsEachWeekByTheNextOneStart()
    {
        var weeks = new (int, DateOnly, DateTimeOffset?)[]
        {
            (0, new DateOnly(2026, 3, 17), null),
            (1, new DateOnly(2026, 3, 24), null),
            (2, new DateOnly(2026, 3, 31), null),
        };

        var windows = RaceWeekWindow.ForSeason(weeks);

        Assert.Equal(new DateTimeOffset(2026, 3, 24, 0, 0, 0, TimeSpan.Zero), windows[0].Window.End);
        Assert.Equal(new DateTimeOffset(2026, 3, 31, 0, 0, 0, TimeSpan.Zero), windows[1].Window.End);
        // The final week has no successor, so it falls back to seven days.
        Assert.Equal(new DateTimeOffset(2026, 4, 7, 0, 0, 0, TimeSpan.Zero), windows[2].Window.End);
    }

    [Fact]
    public void ForSeason_PrefersEachWeekOwnReportedEnd()
    {
        var reported = new DateTimeOffset(2026, 3, 23, 21, 0, 0, TimeSpan.Zero);
        var weeks = new (int, DateOnly, DateTimeOffset?)[]
        {
            (0, new DateOnly(2026, 3, 17), reported),
            (1, new DateOnly(2026, 3, 24), null),
        };

        var windows = RaceWeekWindow.ForSeason(weeks);

        Assert.Equal(reported, windows[0].Window.End);
        Assert.True(windows[0].Window.IsExact);
        Assert.False(windows[1].Window.IsExact);
    }

    [Fact]
    public void ForSeason_OrdersByStartDateRatherThanEnumerationOrder()
    {
        // Nothing guarantees the rows arrive sorted, and an out-of-order pair would otherwise
        // bound a week by a start date that precedes it.
        var weeks = new (int, DateOnly, DateTimeOffset?)[]
        {
            (2, new DateOnly(2026, 3, 31), null),
            (0, new DateOnly(2026, 3, 17), null),
            (1, new DateOnly(2026, 3, 24), null),
        };

        var windows = RaceWeekWindow.ForSeason(weeks);

        Assert.Equal([0, 1, 2], windows.Select(w => w.RaceWeekIndex));
        Assert.Equal(new DateTimeOffset(2026, 3, 24, 0, 0, 0, TimeSpan.Zero), windows[0].Window.End);
    }

    [Fact]
    public void ForSeason_NoWeeks_IsEmpty()
    {
        Assert.Empty(RaceWeekWindow.ForSeason([]));
    }
}
