using ApexRacers.Core;
using ApexRacers.Data;

namespace ApexRacers.Api.Services;

public interface IDriverLifecycleObserver
{
    Task PhaseAsync(string phase, Guid operationId, CancellationToken ct = default);
}

/// <summary>Coordinates the independent veto journal with primary grants and actual writer drain.</summary>
public sealed class DriverAuthorization(
    DriverAuthorityStore store, IDriverEnforcementJournal journal, IDriverOwnershipProof proof,
    TimeProvider timeProvider, IDriverLifecycleObserver? observer = null)
{
    public async Task<DriverAccess> GrantAsync(DriverScope scope, DriverConsent consent, CancellationToken ct = default)
    {
        // No registered-client/provider ownership contract has been verified. Even a caller-supplied
        // synthetic adapter cannot turn this controlled evidence into Real authorization.
        if (scope.Provenance != DataProvenance.Demo)
            throw new InvalidOperationException("Verified Driver ownership is unavailable.");
        var receipt = await proof.VerifyAsync(scope, ct);
        if (receipt is null || receipt.Scope != scope)
            throw new InvalidOperationException("Verified Driver ownership is unavailable.");
        var enforcement = await journal.ReadAsync(scope, ct);
        if (!enforcement.Available || enforcement.PendingIntents.Count != 0)
            throw new InvalidOperationException("Driver enforcement is unavailable.");
        var existing = await store.FindGrantAsync(scope, ct);
        if (existing is not null && existing.Revision < enforcement.MinimumRevision)
            throw new InvalidOperationException("Driver enforcement requires reconciliation.");
        return await store.GrantAsync(receipt, consent, journal, ct);
    }

    public async Task<DriverAccess?> ResolveAsync(DriverScope scope, DriverConsentScope purpose, CancellationToken ct = default)
    {
        if (scope.Provenance != DataProvenance.Demo) return null;
        var enforcement = await journal.ReadAsync(scope, ct);
        if (!enforcement.Available) return null;
        var access = await store.ResolveAsync(scope, purpose, ct);
        return access is not null && enforcement.Allows(access.Revision, purpose) ? access : null;
    }

    public async Task<DriverLifecycleOutcome> TransitionAsync(
        DriverScope scope, DriverLifecycleKind kind, Guid operationId, CancellationToken ct = default)
    {
        if (operationId == Guid.Empty || !Enum.IsDefined(kind)) throw new ArgumentException("A valid lifecycle operation is required.");
        var grant = await store.FindGrantAsync(scope, ct)
            ?? throw new InvalidOperationException("Driver authorization is unavailable.");
        var intent = new DriverLifecycleIntent(operationId, grant.Id, scope, kind,
            DriverAuthorizationPolicy.DurableTime(timeProvider.GetUtcNow()));
        await PhaseAsync("before-journal", operationId, ct);
        intent = await journal.AppendAsync(intent, ct);
        await PhaseAsync("journal-intent-recorded", operationId, ct);
        return await RecoverAsync(intent, ct);
    }

    /// <summary>Replay only the journal's original durable intent; recovery cannot change its clock.</summary>
    public async Task<DriverLifecycleOutcome> RecoverAsync(DriverLifecycleIntent intent, CancellationToken ct = default)
    {
        // Append returns the immutable canonical operation, including after a lost first response.
        var recorded = await journal.AppendAsync(intent, ct);
        if (recorded.Scope != intent.Scope || recorded.GrantId != intent.GrantId || recorded.Kind != intent.Kind)
            throw new InvalidOperationException("Lifecycle recovery does not match the recorded intent.");
        var operation = await store.ApplyIntentAsync(recorded, ct);
        await PhaseAsync("primary-closed", recorded.OperationId, ct);
        var pending = await store.PendingWritersAsync(recorded, ct);
        if (pending != 0) return new(recorded.OperationId, false, pending, recorded.OriginalLossAt);
        await PhaseAsync("drained", recorded.OperationId, ct);
        await journal.ReconcileAsync(recorded, operation.AppliedRevision, ct);
        await PhaseAsync("journal-completed", recorded.OperationId, ct);
        await store.CompleteOperationAsync(recorded, ct);
        return new(recorded.OperationId, true, 0, recorded.OriginalLossAt);
    }

    private Task PhaseAsync(string phase, Guid id, CancellationToken ct) => observer?.PhaseAsync(phase, id, ct) ?? Task.CompletedTask;
}
