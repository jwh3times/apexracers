namespace ApexRacers.Core;

/// <summary>
/// Where a Subject Driver's Personal Best places within a Field, and the Field's median lap.
///
/// <para><b>The interface takes the <em>other</em> Drivers' laps, not the whole Field.</b> The
/// Subject Driver's own entry cannot be handed in, because it may not be the value being ranked:
/// an Uploaded Lap can supersede their Race Best, so the Field row bearing their Customer ID is
/// the wrong number. Every metric here therefore reconstructs the Field as
/// <c>otherDriversLaps + the ranked lap</c> — the Subject Driver is always in the population
/// exactly once, and callers cannot double-count them by passing a Field that still contains them.</para>
///
/// <para><b>These are Field-wide metrics, not head-to-head ones.</b> <see cref="Rank"/> is a
/// percentile rank in the ordinary statistical sense — the Subject Driver counts in its own
/// denominator, and Drivers on an identical lap split the tie rather than being ignored. That is a
/// deliberate change from the earlier formula, which divided strictly-slower Drivers by the count
/// of *other* Drivers and so reported 100 for anyone who merely beat everyone they were compared
/// with, however small that group was. See <c>CONTEXT.md</c> for the recorded definitions.</para>
/// </summary>
public static class FieldPercentile
{
    /// <summary>
    /// The smallest Field whose competitiveness metrics are informative enough to present as a
    /// headline. Smaller Fields still describe how many Drivers set a time, but their coarse
    /// positions are not presented as Percentile Ranks or Top Shares.
    /// </summary>
    public const int MinimumPresentableFieldSize = 5;

    public static bool IsPresentable(int fieldSize) =>
        fieldSize >= MinimumPresentableFieldSize;

    /// <summary>
    /// The Subject Driver's percentile rank within the Field — the share of the Field they are at
    /// least as fast as, with Drivers on an identical lap splitting the tie. Higher is better.
    /// </summary>
    /// <param name="driverBest">
    /// The lap being ranked. May be faster than this Driver's own Race Best when an Uploaded Lap
    /// supersedes it.
    /// </param>
    /// <param name="otherDriversLaps">
    /// Every <em>other</em> Driver's best lap. <b>Must not contain the ranked Driver's own lap.</b>
    /// A Field whose only member is the Subject Driver yields 50 — alone, they are its median.
    /// </param>
    public static double Rank(double driverBest, IReadOnlyCollection<double> otherDriversLaps)
    {
        var fieldSize = otherDriversLaps.Count + 1;
        var slower = otherDriversLaps.Count(lap => lap > driverBest);

        // The Subject Driver is one of the Drivers on their own lap time, so the tied group always
        // has at least one member. Splitting it is what makes two identical laps rank equally
        // instead of one of them counting as beaten.
        var tied = otherDriversLaps.Count(lap => lap == driverBest) + 1;

        return (slower + 0.5 * tied) * 100.0 / fieldSize;
    }

    /// <summary>
    /// The Subject Driver's one-based finishing position within the Field by lap time. Drivers on
    /// an identical lap share the position, so two Drivers tied for the fastest lap are both 1st.
    /// </summary>
    public static int Position(double driverBest, IReadOnlyCollection<double> otherDriversLaps) =>
        otherDriversLaps.Count(lap => lap < driverBest) + 1;

    /// <summary>
    /// The Subject Driver's <see cref="Position"/> as a whole-number share of the Field, rounded up
    /// — the "top X%" a Driver is shown. First of two Drivers is the top 50%, not the top 1%.
    /// </summary>
    public static int TopSharePercent(double driverBest, IReadOnlyCollection<double> otherDriversLaps)
    {
        var fieldSize = otherDriversLaps.Count + 1;
        return (int)Math.Ceiling(Position(driverBest, otherDriversLaps) * 100.0 / fieldSize);
    }

    /// <summary>
    /// The size of the Field the metrics above are computed over: every other Driver plus the
    /// Subject Driver. Callers report this rather than the row count they queried, which counts the
    /// Subject Driver only when they happened to have raced.
    /// </summary>
    public static int FieldSize(IReadOnlyCollection<double> otherDriversLaps) =>
        otherDriversLaps.Count + 1;

    /// <summary>
    /// Median of a list already sorted ascending — the precondition is in the name because every
    /// caller sorts anyway (for the field-best lap or a distribution), so sorting again would be
    /// wasted work rather than safety.
    /// </summary>
    public static double MedianOfSorted(IReadOnlyList<double> sortedLaps)
    {
        ArgumentOutOfRangeException.ThrowIfZero(sortedLaps.Count);

        var mid = sortedLaps.Count / 2;
        return sortedLaps.Count % 2 == 0
            ? (sortedLaps[mid - 1] + sortedLaps[mid]) / 2.0
            : sortedLaps[mid];
    }
}
