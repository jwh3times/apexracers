using ApexRacers.Core.Models;

namespace ApexRacers.Core.PercentileHistory;

/// <summary>Internal calculation data, never read/persistence authority or a protected HTTP result.</summary>
public sealed record RefreshedPercentile(Guid UserId, int CustomerId, Guid WeekId, int CarId, int SeriesId,
    PersonalBest? PersonalBest, LapSessionType? UploadedSessionType, IReadOnlyList<double> OtherDriversLaps,
    double? PercentileRank, int? FieldPosition, int? TopSharePercent, int? FieldSize, bool IsPresentable,
    DateTimeOffset ComputedAt, PercentileSelection Selection, DateOnly WeekStartDate);
/// <summary>Source reads/rows include initial and coordinated final reads; catalog, authority and
/// mutation bookkeeping are excluded. PostgreSQL performance tests separately count total commands.</summary>
public sealed record PercentileRefreshMeasurements(int SourceQueries, int MaterializedSourceRows,
    double ArithmeticMilliseconds, double WallMilliseconds);
public sealed record PercentileRefreshBatch(IReadOnlyList<RefreshedPercentile> Readings, int ChangedRows,
    PercentileRefreshMeasurements Measurements);
