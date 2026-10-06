using ApexRacers.Core.Models;

namespace ApexRacers.Core;

public sealed record PrivateUploadReceipt(DriverAccess Owner, Guid PurposeId, long Generation, long PurposeVersion,
    DateTimeOffset OriginalAcquiredAt);
public sealed record PrivateLap(int LapNumber, double LapTimeSeconds);
public sealed record PrivateUploadData(int CarId, int TrackId, DateTimeOffset RecordedAt, LapSessionType SessionType,
    IReadOnlyList<PrivateLap> Laps);
public sealed record PrivateUploadOutcome(bool Persisted, int TotalLaps, int ValidLaps, double? BestLapSeconds);
public sealed record PrivateUploadedBest(int CarId, int TrackId, string CarName, string TrackName, string? ConfigName,
    double BestLapSeconds, int LapCount, DateTimeOffset LastRecordedAt);
public sealed record PrivateUploadSnapshot(PrivateUploadReceipt Preparation, IReadOnlyList<PrivateUploadedBest> Bests,
    IReadOnlyList<EvidenceSourceVersion> Sources);
public sealed record PrivateOwnerPercentile(double LapSeconds, LapEvidence Evidence, double PercentileRank,
    int FieldPosition, int TopSharePercent, int FieldSize);
public sealed record PrivateCleanupStatus(bool WithdrawalCompleted, int RetainedCopies, int OverdueCopies,
    bool LiveErasureVerified, DateTimeOffset? LiveRemovalDueAt, DateTimeOffset? BackupExpiryDueAt,
    bool BackupExpiryVerified = false);
