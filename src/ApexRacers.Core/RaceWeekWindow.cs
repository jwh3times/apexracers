namespace ApexRacers.Core;

/// <summary>
/// The span of time a Race Week covers, and whether a Lap was driven inside it.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> A Race Best belongs to one Race Week; an Uploaded Best was scoped
/// across every Telemetry Upload a User holds. Ranking the faster of the two against one Race
/// Week's Field silently compared laps from different time periods — a dry practice lap from a
/// previous season, on a different build and a different BoP, could take the percentile from a wet
/// Race Week's Race Best. Bounding the uploaded side to the same Race Week makes both sides of the
/// comparison mean the same thing.</para>
///
/// <para><b>Why the end is not start + 7 days.</b> iRacing reports an exact
/// <c>week_end_time</c> — <c>2026-03-23T21:00:00Z</c> for a Week starting <c>2026-03-17</c> — which
/// is 21:00 UTC on the seventh day, not midnight. Reconstructing it arithmetically would be three
/// hours long every week, quietly admitting laps from after the Week closed.</para>
///
/// <para><b>Why a fallback exists at all.</b> Weeks ingested before the column existed have no end
/// time, and treating those as open-ended would reintroduce the very bug this closes. The next
/// Week's start is the true boundary when it is known — Weeks are contiguous — and seven days is
/// the last resort for a season's final Week. Both fallbacks are marked
/// <see cref="IsExact"/> = false so a caller can tell a derived boundary from a reported one.</para>
/// </remarks>
public readonly record struct RaceWeekWindow(DateTimeOffset Start, DateTimeOffset End, bool IsExact)
{
    /// <summary>A Race Week is seven days long when nothing better is known.</summary>
    private const int NominalLengthDays = 7;

    /// <summary>
    /// The window for a Race Week starting on <paramref name="startDate"/>.
    /// <paramref name="reportedEnd"/> is iRacing's <c>week_end_time</c> when it was ingested;
    /// <paramref name="nextWeekStart"/> is the following Week's start date, used only when no end
    /// was reported.
    /// </summary>
    public static RaceWeekWindow For(
        DateOnly startDate,
        DateTimeOffset? reportedEnd = null,
        DateOnly? nextWeekStart = null)
    {
        var start = AtMidnightUtc(startDate);

        if (reportedEnd is { } exact && exact > start)
            return new RaceWeekWindow(start, exact, IsExact: true);

        // Weeks are contiguous, so the next one's start is where this one stops.
        if (nextWeekStart is { } next && next > startDate)
            return new RaceWeekWindow(start, AtMidnightUtc(next), IsExact: false);

        return new RaceWeekWindow(start, start.AddDays(NominalLengthDays), IsExact: false);
    }

    /// <summary>
    /// Windows for every Race Week of one season, each bounded by the following Week's start when
    /// it reported no end of its own. Ordered by start date, with the Race Week Index breaking a tie —
    /// the same ordering <c>SeasonCalendar</c> uses, so "the next Week" means the same thing in
    /// both places.
    /// </summary>
    public static IReadOnlyList<(int RaceWeekIndex, RaceWeekWindow Window)> ForSeason(
        IEnumerable<(int RaceWeekIndex, DateOnly StartDate, DateTimeOffset? EndTime)> weeks)
    {
        var ordered = weeks.OrderBy(w => w.StartDate).ThenBy(w => w.RaceWeekIndex).ToList();

        return ordered
            .Select((week, i) => (
                week.RaceWeekIndex,
                Window: For(
                    week.StartDate,
                    week.EndTime,
                    i + 1 < ordered.Count ? ordered[i + 1].StartDate : null)))
            .ToList();
    }

    /// <summary>
    /// Whether a Lap driven at <paramref name="recordedAt"/> falls inside this Race Week. The start
    /// is inclusive and the end exclusive, so contiguous Weeks cannot both claim the same Lap.
    /// </summary>
    public bool Contains(DateTimeOffset recordedAt) => recordedAt >= Start && recordedAt < End;

    private static DateTimeOffset AtMidnightUtc(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
