using System.Collections.Immutable;
using ApexRacers.Core;
using ApexRacers.Core.Models;

namespace ApexRacers.Api.Services.PercentileHistory;

/// <summary>Data identity only, never permission to read, persist or dispatch.</summary>
public sealed record PercentileHistoryContext(PercentileOwnerIdentity Owner, Guid? GrantId, long? OwnerRevision,
    PercentileSelection Selection, int CarId, int SeriesId);
public sealed record PercentileHistoryWeek(Guid Id, int SeasonId, DateOnly StartDate, int TrackId);
public sealed record PercentileHistoryPreparation(Guid PurposeId, long Generation, long Version);
public sealed record PercentileHistorySource(Guid CopyId, long Version);

/// <summary>One bounded single-Week Car batch; the implementation must resolve the owner again,
/// read currently authorized retained sources, and preserve preparation versions and original copy
/// clocks. A request is data, not a write capability. No refresh implementation is introduced here.</summary>
public sealed record PercentileHistoryRefreshRequest(PercentileOwnerIdentity Owner, PercentileSelection Selection,
    int SeriesId, PercentileHistoryWeek Week, ImmutableArray<int> CarIds);

/// <summary>Classified calculation snapshot, not proof of current retained source availability.
/// Each reading carries its own Personal Best, selection/owner context and preparation revision.
/// Missing Personal Best or undersized Field remains diagnostic, never a headline contribution.</summary>
public sealed record PercentileHistoryReading(PercentileHistoryContext Context, PercentileHistoryWeek Week,
    PersonalBest? PersonalBest, int FieldSize, double? PercentileRank, int? TopSharePercent,
    long ReadingRevision, PercentileHistoryPreparation Preparation, ImmutableArray<PercentileHistorySource> Sources,
    LapSessionType? UploadedSessionType = null);

/// <summary>Coherent internal result. Protected endpoints must keep this inside their owned
/// executor through final authority/source checks and writer terminality; this is not an HTTP DTO.</summary>
public sealed record PercentileHistoryResult(PercentileHistoryContext Context, PercentileHistoryWeek RequestedWeek,
    ImmutableArray<PercentileHistoryReading> Contributions, double? ExpectedPercentile);
