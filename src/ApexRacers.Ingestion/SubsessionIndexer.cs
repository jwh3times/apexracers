namespace ApexRacers.Ingestion;

/// <summary>
/// Pure decision and mapping helpers extracted from <see cref="Worker"/> so the
/// subsession-indexing logic can be unit-tested without the iRacing API or a database.
/// <see cref="Worker"/> remains the thin orchestration shell that performs the I/O.
/// </summary>
public static class SubsessionIndexer
{
    /// <summary>Lap-time value stored when a result has no recorded lap.</summary>
    public const double NoLapSentinel = -1;

    /// <summary>
    /// Narrows the official-results search window to just after the last indexed start,
    /// minus a one-hour buffer that catches concurrent splits. A null last-start (the
    /// first run for a season) returns null → fetch the entire season.
    /// </summary>
    public static DateTime? ComputeSearchRangeBegin(DateTimeOffset? lastIndexedStart) =>
        lastIndexedStart?.UtcDateTime.AddHours(-1);

    /// <summary>
    /// Candidate subsession IDs that are not already stored, de-duplicated.
    /// </summary>
    public static List<int> ComputeNewSubsessionIds(
        IEnumerable<int> candidateIds, IEnumerable<int> existingIds) =>
        candidateIds.Distinct().Except(existingIds).ToList();

    /// <summary>
    /// Series display name: the first schedule entry's name, or a synthesized
    /// fallback when the schedule supplies none.
    /// </summary>
    public static string ResolveSeriesName(string? scheduleSeriesName, int seriesId) =>
        string.IsNullOrWhiteSpace(scheduleSeriesName) ? $"Series {seriesId}" : scheduleSeriesName;

    /// <summary>
    /// Split number = the position of this subsession within its session-splits ordering.
    /// Returns 0 when there are no splits or the subsession is not found among them.
    /// </summary>
    public static int ResolveSplitNumber(IReadOnlyList<int>? splitSubsessionIds, int subsessionId)
    {
        if (splitSubsessionIds is null || splitSubsessionIds.Count == 0)
            return 0;

        for (var i = 0; i < splitSubsessionIds.Count; i++)
        {
            if (splitSubsessionIds[i] == subsessionId)
                return i;
        }

        return 0;
    }

    /// <summary>
    /// AI drivers and team entries (which have no customer ID) are not indexed.
    /// </summary>
    public static bool ShouldSkipResult(bool isAi, long? customerId) =>
        isAi || customerId is null;

    /// <summary>
    /// A driver's lap time in seconds, or <see cref="NoLapSentinel"/> when absent.
    /// </summary>
    public static double LapSecondsOrSentinel(TimeSpan? lap) =>
        lap?.TotalSeconds ?? NoLapSentinel;
}
