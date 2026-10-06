namespace ApexRacers.Core;

public enum DriverConsentScope { Personal = 1, Sharing = 2 }
public enum DriverLifecycleKind { WithdrawSharing = 1, WithdrawPersonal = 2, Unlink = 3, RevokeProof = 4, DeleteUser = 5 }
public sealed record DriverScope(Guid UserId, int CustomerId, DataProvenance Provenance);
public sealed record VerifiedDriverProof(Guid ReceiptId, DriverScope Scope, DateTimeOffset VerifiedAt, string Authority, string DriverName);
public sealed record DriverConsent(string PersonalVersion, string? SharingVersion = null);
public sealed record DriverAccess(DriverScope Scope, Guid GrantId, long Revision, DriverConsentScope Purpose, string? DriverName);
public sealed record DriverLifecycleIntent(Guid OperationId, Guid GrantId, DriverScope Scope, DriverLifecycleKind Kind, DateTimeOffset OriginalLossAt);
public sealed record DriverLifecycleOutcome(Guid OperationId, bool Completed, int PendingWriters, DateTimeOffset OriginalLossAt);
public sealed record DriverUserEnforcement(bool Available, DriverLifecycleIntent? Deletion = null);
public sealed record DriverJournalState(bool Available, long MinimumRevision, IReadOnlyList<DriverLifecycleIntent> PendingIntents)
{
    public bool Allows(long revision, DriverConsentScope purpose) => Available && revision >= MinimumRevision
        && !PendingIntents.Any(i => i.Kind != DriverLifecycleKind.WithdrawSharing || purpose == DriverConsentScope.Sharing);
}

/// <summary>The separately protected journal vetoes; no entry can grant access.</summary>
public interface IDriverEnforcementJournal
{
    // A deletion veto covers every historical and future Driver association of the User.
    // Adapters without this contract cannot authorize personal acquisition/publication.
    Task<DriverUserEnforcement> ReadUserAsync(Guid userId, CancellationToken ct = default) =>
        Task.FromResult(new DriverUserEnforcement(false));
    Task<DriverJournalState> ReadAsync(DriverScope scope, CancellationToken ct = default);
    Task<DriverLifecycleIntent> AppendAsync(DriverLifecycleIntent intent, CancellationToken ct = default);
    Task ReconcileAsync(DriverLifecycleIntent intent, long revision, CancellationToken ct = default);
}

public static class DriverEnforcement
{
    public static async Task<DriverJournalState> ReadCurrentAsync(this IDriverEnforcementJournal journal,
        DriverScope scope, CancellationToken ct = default)
    {
        var user = await journal.ReadUserAsync(scope.UserId, ct);
        if (user is not { Available: true }) return new(false, 0, []);
        var state = await journal.ReadAsync(scope, ct);
        return user.Deletion is null ? state : state with { PendingIntents = [.. state.PendingIntents, user.Deletion] };
    }

    public static Guid AssociationOperationId(Guid rootOperationId, Guid grantId) =>
        new(System.Security.Cryptography.SHA256.HashData(rootOperationId.ToByteArray().Concat(grantId.ToByteArray()).ToArray()).AsSpan(0, 16));
}

public interface IDriverOwnershipProof
{
    Task<VerifiedDriverProof?> VerifyAsync(DriverScope scope, CancellationToken ct = default);
}

public sealed class UnavailableDriverOwnershipProof : IDriverOwnershipProof
{
    public Task<VerifiedDriverProof?> VerifyAsync(DriverScope scope, CancellationToken ct = default) => Task.FromResult<VerifiedDriverProof?>(null);
}

public sealed class UnavailableDriverEnforcementJournal : IDriverEnforcementJournal
{
    public Task<DriverJournalState> ReadAsync(DriverScope scope, CancellationToken ct = default) =>
        Task.FromResult(new DriverJournalState(false, 0, []));
    public Task<DriverLifecycleIntent> AppendAsync(DriverLifecycleIntent intent, CancellationToken ct = default) =>
        Task.FromException<DriverLifecycleIntent>(new InvalidOperationException("Driver enforcement is unavailable."));
    public Task ReconcileAsync(DriverLifecycleIntent intent, long revision, CancellationToken ct = default) =>
        Task.FromException(new InvalidOperationException("Driver enforcement is unavailable."));
}

public static class DriverAuthorizationPolicy
{
    public const string PersonalConsentVersion = "personal-v1";
    public const string SharingConsentVersion = "sharing-v1";
    public static DateTimeOffset DurableTime(DateTimeOffset time) => new(time.UtcTicks - time.UtcTicks % 10, TimeSpan.Zero);
    public static bool Affects(DriverLifecycleKind kind, DriverConsentScope purpose) =>
        kind != DriverLifecycleKind.WithdrawSharing || purpose == DriverConsentScope.Sharing;
    public static DateTimeOffset CleanupDueAt(DriverLifecycleKind kind, DriverConsentScope purpose, DateTimeOffset lossAt) =>
        purpose == DriverConsentScope.Sharing ? lossAt.AddHours(24)
        : kind == DriverLifecycleKind.DeleteUser ? lossAt.AddDays(7) : lossAt.AddDays(97);
}
