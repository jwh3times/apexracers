namespace ApexRacers.Core;

/// <summary>
/// How a driver's best lap ranks against the rest of the field, and the field's median lap.
///
/// <para><b>Why this exists.</b> The rank formula was written five times — twice in the percentile
/// service and recommendation service with the driver excluded by customer id, and three times
/// against a bare list of lap times with no exclusion at all. Those two conventions produce the
/// same number today <em>only because <c>&gt;</c> is strict</em>: a driver's own lap is never
/// slower than itself, so leaving it in the count happens to be harmless. Change any one site to
/// <c>&gt;=</c> — to count a tie as beaten, say — and the unfiltered sites start counting the
/// driver as slower than themselves while the filtered ones stay correct. Nothing in the type
/// system or the tests connected them.</para>
///
/// <para><b>The interface takes the <em>other</em> drivers' laps, not the whole field.</b> That is
/// the simplification the duplication was hiding: once the driver is excluded, the old
/// "is the driver in the field?" branch disappears. The denominator was
/// <c>driverInField ? total - 1 : total</c> at every site, and both arms are just "how many other
/// drivers are there". Making the caller hand over the field without themselves in it removes a
/// parameter, removes a branch, and makes the exclusion impossible to forget — it is the first
/// thing the signature asks for.</para>
///
/// <para>Excluding the driver is not merely tidy. Their own lap may be <em>slower</em> than the
/// value being ranked, because a personal (non-race) lap can supersede their race result. Left in
/// the field, that slower lap counts as one more driver beaten and inflates the rank.</para>
/// </summary>
public static class FieldPercentile
{
    /// <summary>
    /// Percentage of <paramref name="otherDriversLaps"/> slower than <paramref name="driverBest"/>,
    /// so higher is better.
    /// </summary>
    /// <param name="driverBest">
    /// The lap being ranked. May be faster than this driver's own race lap when a personal lap
    /// supersedes it.
    /// </param>
    /// <param name="otherDriversLaps">
    /// Every other driver's best lap. <b>Must not contain the ranked driver's own lap.</b> An empty
    /// field yields 100 — with nobody to compare against, the driver is trivially first.
    /// </param>
    public static double Rank(double driverBest, IReadOnlyCollection<double> otherDriversLaps)
    {
        if (otherDriversLaps.Count == 0) return 100.0;

        var slower = otherDriversLaps.Count(lap => lap > driverBest);
        return slower * 100.0 / otherDriversLaps.Count;
    }

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
