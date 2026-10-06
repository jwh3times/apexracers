using ApexRacers.Core.Models;

namespace ApexRacers.Core;

public enum EvidencePurposeKind { SyntheticPreview = 1, IndependentOfficial = 2, AuthorizedHistory = 3, Personal = 4, Sharing = 5 }
public enum EvidenceCopyKind { MappedCache = 1, OfficialField = 2, Bop = 3, Weather = 4, PersonalDerivative = 5, Follow = 6, AuthorizedName = 7, PrivateUpload = 8 }

/// <summary>Captured before preparation; a receipt is neither a consent grant nor a publication admission.</summary>
public sealed record EvidenceWriteReceipt(Guid PurposeId, long Generation, long PurposeVersion, EvidenceCopyKind Kind,
    string KeyHash, long ExpectedCopyVersion, DateTimeOffset OriginalAcquiredAt,
    IReadOnlyList<EvidenceSourceVersion> Sources);
public sealed record EvidenceSourceVersion(Guid CopyId, long Version);
public sealed record EvidenceCollectionRequest(DataProvenance Provenance, EvidencePurposeKind Purpose, int? SeasonId = null,
    DriverAccess? Owner = null, Guid? PublicationAdmissionId = null);
public interface IEvidencePurposeIssuer
{
    Task<Guid?> ResolveAsync(EvidenceCollectionRequest request, CancellationToken ct = default);
}
public sealed class UnavailableEvidencePurposeIssuer : IEvidencePurposeIssuer
{
    public Task<Guid?> ResolveAsync(EvidenceCollectionRequest request, CancellationToken ct = default) => Task.FromResult<Guid?>(null);
}
public sealed record EvidenceCleanupOutcome(int VerifiedRemoved, int Pending, int Overdue, bool BackupExpiryVerified = false);

// Closed persistence families. Neither SDK objects nor an arbitrary database callback can cross this seam.
public abstract record EvidenceBatch
{
    private protected EvidenceBatch() { }
}
public sealed record MappedCacheBatch(ExternalDataCache Copy) : EvidenceBatch;
public sealed record OfficialFieldBatch(Subsession Race, IReadOnlyList<SubsessionResult> Results) : EvidenceBatch;
public sealed record BopBatch(SeasonCarBop Copy) : EvidenceBatch;
public sealed record WeatherBatch(Guid WeekId, string Payload) : EvidenceBatch;
public sealed record ScheduleEvidenceBatch(IReadOnlyList<SeasonCarBop> Bops, IReadOnlyList<WeatherBatch> Weather) : EvidenceBatch;
public sealed record PercentileBatch(CarPercentileResult Copy) : EvidenceBatch;
public sealed record FollowBatch(Rival Copy) : EvidenceBatch;
public sealed record AuthorizedNameBatch(Guid GrantId) : EvidenceBatch;

public interface IManagedEvidence : IProvenancedData
{
    Guid? EvidenceCopyId { get; set; }
}

public static class EvidenceRetention
{
    public static DateTimeOffset MappedRemovalDueAt(DateTimeOffset expiresAt) => expiresAt.AddHours(48);
    public static DateTimeOffset PurposeRemovalDueAt(DateTimeOffset originalEndAt) => originalEndAt.AddDays(7);
    public static DateTimeOffset NameRemovalDueAt(DateTimeOffset originalLossAt) => originalLossAt.AddHours(24);
    public static DateTimeOffset Earliest(DateTimeOffset first, DateTimeOffset second) => first < second ? first : second;
}

public sealed class EvidenceCopyUnavailableException() : Exception("The evidence collection scope is unavailable.");
