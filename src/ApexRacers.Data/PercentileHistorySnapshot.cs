using System.Collections.ObjectModel;
using ApexRacers.Core.PercentileHistory;

namespace ApexRacers.Data;

/// <summary>One opaque current-reading snapshot. Data and metadata are immutable to callers; the
/// private witness rechecks catalog, retained sources and current authority. Possession grants no
/// publication permission; Personal output stays inside its admitted HTTP executor.</summary>
public sealed class PercentileHistorySnapshot
{
    private Func<CancellationToken, Task<bool>> CurrentCheck { get; }
    internal PercentileHistorySnapshot(int customerId, PercentileSelection selection, IReadOnlyList<RefreshedPercentile> readings,
        IEnumerable<PercentileHistoryWeekMetadata> weeks, IEnumerable<PercentileHistorySeasonMetadata> seasons,
        IDictionary<int, string> carNames, Func<CancellationToken, Task<bool>> current)
    {
        CustomerId = customerId; Selection = selection; CurrentCheck = current;
        Readings = Array.AsReadOnly(readings.Select(r => r with { OtherDriversLaps = Array.AsReadOnly(r.OtherDriversLaps.ToArray()) }).OrderBy(r => r.WeekStartDate).ThenBy(r => r.WeekId).ThenBy(r => r.CarId).ToArray());
        Weeks = Array.AsReadOnly(weeks.OrderBy(w => w.StartDate).ThenBy(w => w.WeekId).ToArray());
        Seasons = Array.AsReadOnly(seasons.OrderBy(s => s.SeasonId).ToArray());
        CarNames = new ReadOnlyDictionary<int, string>(new Dictionary<int, string>(carNames));
    }
    public int CustomerId { get; }
    public PercentileSelection Selection { get; }
    public IReadOnlyList<RefreshedPercentile> Readings { get; }
    public IReadOnlyList<PercentileHistoryWeekMetadata> Weeks { get; }
    public IReadOnlyList<PercentileHistorySeasonMetadata> Seasons { get; }
    public IReadOnlyDictionary<int, string> CarNames { get; }
    internal Task<bool> CurrentAsync(CancellationToken ct) => CurrentCheck(ct);
}
