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
    /// One entry of an iRacing <c>session_splits</c> array, reduced to the two fields the Split
    /// Index is derived from. Keeping the SDK's <c>SessionSplit</c> out of this module is what lets
    /// the derivation be unit-tested and keeps SDK drift confined to <see cref="Worker"/>.
    /// </summary>
    public readonly record struct SplitEntry(int SubsessionId, int StrengthOfField);

    /// <summary>
    /// A Subsession's position among the Splits of its Race Session: a zero-based
    /// <paramref name="Index"/> ordered by Strength of Field descending, and how many Splits there
    /// were in total. The two are only ever produced together, from one payload.
    /// </summary>
    public readonly record struct SplitPosition(int Index, int Count);

    /// <summary>
    /// Ranks <paramref name="subsessionId"/> among <paramref name="splits"/> by Strength of Field
    /// descending, so Split Index 0 is the strongest Split. The rank is computed from the SOF each
    /// entry carries rather than from the array's order, because nothing in iRacing's contract
    /// promises <c>session_splits</c> arrives sorted; equal SOF is broken by ascending subsession
    /// id so the index is stable across re-reads of the same payload.
    /// </summary>
    /// <returns>
    /// Null when the Split Index is unknown — no splits were supplied, or this Subsession is absent
    /// from the ones that were (a payload that disagrees with itself). Null is deliberately not
    /// index 0: an unknown position must never read as "the strongest Split".
    /// </returns>
    public static SplitPosition? ResolveSplitPosition(
        IReadOnlyList<SplitEntry>? splits, int subsessionId)
    {
        if (splits is null || splits.Count == 0)
            return null;

        SplitEntry? self = null;
        foreach (var split in splits)
        {
            if (split.SubsessionId == subsessionId)
            {
                self = split;
                break;
            }
        }

        if (self is not { } entry)
            return null;

        var index = 0;
        foreach (var other in splits)
        {
            if (other.SubsessionId == subsessionId)
                continue;

            if (other.StrengthOfField > entry.StrengthOfField ||
                (other.StrengthOfField == entry.StrengthOfField && other.SubsessionId < subsessionId))
            {
                index++;
            }
        }

        return new SplitPosition(index, splits.Count);
    }

    /// <summary>
    /// What one entry of a race's result list becomes. Only a <see cref="Classified"/> entry
    /// produces a Race Result, so the other two are exactly the entries a stored field is
    /// missing — counting them is what lets an incomplete field be told from a complete one.
    /// </summary>
    public enum EntryDisposition
    {
        /// <summary>Names one Driver by Customer ID, so it becomes a Race Result.</summary>
        Classified,

        /// <summary>
        /// An AI driver. It holds no iRacing racing identity, so it is not a Driver and cannot
        /// be named by a Race Result — but it did occupy a finishing position in the race.
        /// </summary>
        AiEntry,

        /// <summary>
        /// A team entry. It raced under no Customer ID, which is what the domain language calls
        /// a team entry, so no single Driver can be named and no Race Result is produced. The
        /// entry was a real competitor and held a finishing position all the same.
        /// </summary>
        TeamEntry,
    }

    /// <summary>How a race's entries divided between the three dispositions.</summary>
    public readonly record struct EntryTally(int Classified, int AiEntries, int TeamEntries);

    /// <summary>
    /// Classifies one result entry. AI is checked first because an AI driver may still carry a
    /// Customer ID; after that, an entry with no Customer ID is a team entry by definition.
    /// </summary>
    public static EntryDisposition ClassifyEntry(bool isAi, long? customerId) =>
        isAi ? EntryDisposition.AiEntry
        : customerId is null ? EntryDisposition.TeamEntry
        : EntryDisposition.Classified;

    /// <summary>
    /// Counts a race's entries by disposition, so a Subsession can record how much of its field
    /// it could not represent instead of presenting the remainder as the whole classification.
    /// </summary>
    public static EntryTally TallyEntries(IEnumerable<EntryDisposition> dispositions)
    {
        var classified = 0;
        var ai = 0;
        var team = 0;

        foreach (var disposition in dispositions)
        {
            switch (disposition)
            {
                case EntryDisposition.Classified: classified++; break;
                case EntryDisposition.AiEntry: ai++; break;
                case EntryDisposition.TeamEntry: team++; break;
            }
        }

        return new EntryTally(classified, ai, team);
    }

    /// <summary>
    /// A driver's lap time in seconds, or <see cref="NoLapSentinel"/> when absent.
    /// </summary>
    public static double LapSecondsOrSentinel(TimeSpan? lap) =>
        lap?.TotalSeconds ?? NoLapSentinel;

    /// <summary>
    /// Event-level lap time in seconds. iRacing maps a "no valid lap" (-1) to a
    /// non-positive <see cref="TimeSpan"/>; collapse anything &lt;= 0 to the sentinel.
    /// </summary>
    public static double EventLapSecondsOrSentinel(TimeSpan lap) =>
        lap.TotalSeconds <= 0 ? NoLapSentinel : lap.TotalSeconds;
}
