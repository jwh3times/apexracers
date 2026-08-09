namespace ApexRacers.Core;

/// <summary>
/// Which race week a season is currently in.
///
/// <para><b>Why this exists.</b> Two surfaces answered this question independently and did not
/// agree on how: the series list took the week with the latest <c>StartDate</c>, while the
/// standings page took the highest <c>WeekNumber</c>, both among weeks that had already started.
/// Those coincide only while start dates and week numbers run in the same order — nothing enforces
/// that, and a duplicated or out-of-order start date would have made the series list and the
/// standings page disagree about what week it is, with no shared code to fix.</para>
///
/// <para>The rule below is start date first, week number only to break a tie, which is strictly
/// more defined than either original and matches both on well-ordered data.</para>
///
/// <para><b>What happens before a season starts is deliberately left to the caller.</b> The two
/// surfaces want different things and both are right: the series list shows a blank cell (null),
/// while the standings page has to render <em>some</em> week and falls back to the first. Folding
/// that into this function would have forced one of them to change behaviour for no reason.</para>
/// </summary>
public static class SeasonCalendar
{
    /// <summary>
    /// The latest week that has already started, or <c>null</c> when the season has not begun.
    /// </summary>
    public static int? CurrentWeekNumber(
        IEnumerable<(int WeekNumber, DateOnly StartDate)> weeks,
        DateOnly today)
    {
        (int WeekNumber, DateOnly StartDate)? latest = null;

        foreach (var week in weeks)
        {
            if (week.StartDate > today) continue;

            if (latest is not { } current
                || week.StartDate > current.StartDate
                || (week.StartDate == current.StartDate && week.WeekNumber > current.WeekNumber))
            {
                latest = week;
            }
        }

        return latest?.WeekNumber;
    }
}
