namespace ApexRacers.Core;

/// <summary>
/// One season of a series, reduced to what the current-season rule needs: when its first race week
/// begins (null when the season has no weeks stored yet) and the tiebreakers.
/// </summary>
public readonly record struct SeasonStart(
    int SeasonId,
    DateOnly? FirstRaceWeekStart,
    int Year,
    int Quarter,
    bool Active);

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
    /// The <b>Current Season</b> for one series: the season whose first race week began most
    /// recently.
    ///
    /// <para><b>Why this is not "the newest active season."</b> iRacing marks a season active before
    /// racing starts, and leaves the outgoing one active through the changeover, so during a
    /// changeover a series has two active seasons and the newer one has not begun. Ordering by
    /// year-and-quarter picked that upcoming season, which is how schedule, standings, strategy and
    /// week data could all read next quarter's empty season while this quarter was still running.
    /// Active is an upstream <em>status</em>; it is not a uniqueness guarantee and it is not the
    /// selection rule.</para>
    ///
    /// <para>So the rule is a date: whichever season's first race week start has arrived most
    /// recently is current, and it stays current — through its final week and through the gap
    /// afterwards — until the next season's first race week start date arrives. Active status is
    /// deliberately <em>not</em> a filter here, so a season that upstream has already deactivated
    /// still holds the current slot until its successor actually begins.</para>
    ///
    /// <para><b>The one place active status still decides anything</b> is a series with no season
    /// that has begun at all — a brand-new series, or one whose only stored season is upcoming.
    /// There is no "began most recently" to pick, so this falls back to the newest active season,
    /// which is the behaviour that shipped before. Whether an upcoming-only series should be
    /// presented as current at all is a product question left open on purpose.</para>
    /// </summary>
    /// <returns>The chosen season id, or <c>null</c> when the series has neither a season that has
    /// begun nor an active one.</returns>
    public static int? CurrentSeasonId(IEnumerable<SeasonStart> seasons, DateOnly today)
    {
        var candidates = seasons as IReadOnlyCollection<SeasonStart> ?? seasons.ToList();

        var begun = candidates
            .Where(s => s.FirstRaceWeekStart is { } start && start <= today)
            .ToList();

        // Season id last in both keys only to make the answer total: without it two seasons sharing
        // a first-week date (or a year and quarter) would resolve by enumeration order.
        if (begun.Count > 0)
            return begun
                .MaxBy(s => (s.FirstRaceWeekStart!.Value, s.Year, s.Quarter, s.SeasonId))
                .SeasonId;

        var active = candidates.Where(s => s.Active).ToList();

        return active.Count > 0
            ? active.MaxBy(s => (s.Year, s.Quarter, s.SeasonId)).SeasonId
            : null;
    }

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
