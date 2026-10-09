using System.Collections.Immutable;

namespace ApexRacers.Core;

public enum DriverOperatingStage { Smoke = 1, Pilot = 2, Alpha = 3, Beta = 4, Standard = 5 }
public enum OperatingAudience { Visitor = 0, Standard = 1, Beta = 2, Alpha = 3, Admin = 4 }
public enum OperatingWork { Acquisition = 1, Publication = 2, BackgroundCollection = 3 }
public sealed record OperatingScope(string CatalogId, string CollectionScope, OperatingWork Work, long CatalogRevision = 1);
public sealed record OperatingBudget(int AcquisitionUnits, int PublicationUnits, TimeSpan Window, string EvidenceId, DataProvenance Provenance, bool Verified = false);
public sealed record DriverOperatingManifest(string Id, long ScopeVersion, DataProvenance Provenance,
    ImmutableArray<OperatingScope> Scopes, ImmutableArray<Guid> Allowlist, OperatingBudget? Budget);
public sealed record OperatingOptIn(Guid UserId, long ScopeVersion);
public sealed record OperatingApproval(DriverOperatingStage Stage, DateTimeOffset At, string EvidenceId,
    bool CompositionReviewed, bool CapacityReviewed, bool PrerequisitesVerified, bool VisitorOutputVerified = false, long ScopeVersion = 1);
public sealed record OperatingStop(Guid Id, string Cause, DateTimeOffset At, bool MaterialRepair);
public sealed record OperatingRecovery(Guid StopId, string Cause, string EvidenceId, bool FailureReproduced,
    bool RecoveryVerified, bool PhysicalCleanupVerified, bool UsefulOutputVerified, OperatingApproval Approval);
public sealed record OperatingRequest(string CatalogId, string CollectionScope, OperatingWork Work,
    DataProvenance Provenance, Guid? UserId, OperatingAudience Audience, long CatalogRevision = 1);
public sealed record OperatingLease(Guid Id, long Revision, OperatingRequest Request);
public sealed record DriverOperatingState(DriverOperatingManifest Manifest, DriverOperatingStage Stage,
    DateTimeOffset StageStartedAt, DateTimeOffset PilotStartedAt, long Revision,
    ImmutableArray<OperatingOptIn> OptIns, ImmutableArray<OperatingApproval> Approvals,
    ImmutableArray<OperatingStop> Stops, OperatingStop? ActiveStop)
{
    public ImmutableArray<OperatingRecovery> Recoveries { get; init; } = [];
    public ImmutableArray<DriverOperatingManifest> PreviousScopes { get; init; } = [];
    public DateTimeOffset? SafetyStoppedAt { get; init; }
}

/// <summary>Operating admission supplements proof, consent, catalog and independent history. It
/// never supplies any of them. Implementations must atomically reserve shared budgets.</summary>
public interface IDriverOperatingControls
{
    Task<OperatingLease?> ReserveAsync(OperatingRequest request, CancellationToken ct = default);
    Task<bool> CurrentAsync(OperatingLease lease, CancellationToken ct = default);
}
public sealed class UnavailableDriverOperatingControls : IDriverOperatingControls
{
    public Task<OperatingLease?> ReserveAsync(OperatingRequest request, CancellationToken ct = default) => Task.FromResult<OperatingLease?>(null);
    public Task<bool> CurrentAsync(OperatingLease lease, CancellationToken ct = default) => Task.FromResult(false);
}

public static class DriverOperatingPolicy
{
    public static DriverOperatingState? ChangeScope(DriverOperatingState state, DriverOperatingManifest manifest,
        OperatingApproval smoke, DateTimeOffset now)
    {
        if (manifest.Id != state.Manifest.Id || manifest.ScopeVersion != state.Manifest.ScopeVersion + 1
            || smoke.Stage != DriverOperatingStage.Smoke || smoke.ScopeVersion != manifest.ScopeVersion
            || !ApprovalValid(smoke, now) || smoke.At < state.StageStartedAt) return null;
        var next = state with { Manifest = manifest, Stage = DriverOperatingStage.Smoke,
            StageStartedAt = smoke.At, PilotStartedAt = smoke.At, Revision = state.Revision + 1,
            OptIns = [], Approvals = state.Approvals.Add(smoke), PreviousScopes = state.PreviousScopes.Add(state.Manifest) };
        return Valid(next, now) ? next : null;
    }
    public static DriverOperatingState? Stop(DriverOperatingState state, OperatingStop stop, DateTimeOffset now)
    {
        if (stop.Id == Guid.Empty || string.IsNullOrWhiteSpace(stop.Cause) || stop.Cause.Length > 80
            || stop.At > now || stop.At < state.StageStartedAt) return null;
        if (state.Stops.SingleOrDefault(s => s.Id == stop.Id) is { } known)
            return known == stop && !state.Recoveries.Any(r => r.StopId == stop.Id) ? state : null;
        return state with { ActiveStop = state.ActiveStop ?? stop, Stops = state.Stops.Add(stop), Revision = state.Revision + 1,
            SafetyStoppedAt = state.SafetyStoppedAt is { } original && original < stop.At ? original : stop.At };
    }

    public static DriverOperatingState? Recover(DriverOperatingState state, OperatingRecovery recovery, DateTimeOffset now)
    {
        var stop = state.Stops.SingleOrDefault(s => s.Id == recovery.StopId && !state.Recoveries.Any(r => r.StopId == s.Id));
        if (!Valid(state, now) || state.ActiveStop is null || stop is null || recovery.Cause != stop.Cause
            || string.IsNullOrWhiteSpace(recovery.EvidenceId) || !recovery.FailureReproduced || !recovery.RecoveryVerified
            || !recovery.PhysicalCleanupVerified || !recovery.UsefulOutputVerified
            || recovery.Approval.Stage != DriverOperatingStage.Pilot || !ApprovalValid(recovery.Approval, now)
            || recovery.Approval.At < stop.At || recovery.Approval.ScopeVersion != state.Manifest.ScopeVersion) return null;
        var recoveries = state.Recoveries.Add(recovery);
        var pending = state.Stops.Where(s => !recoveries.Any(r => r.StopId == s.Id)).ToArray();
        if (pending.Length != 0) return state with { ActiveStop = pending[0], Revision = state.Revision + 1, Recoveries = recoveries };
        var originalPilot = state.Stops.Any(s => s.MaterialRepair && s.At >= state.SafetyStoppedAt)
            ? recovery.Approval.At : state.PilotStartedAt;
        return state with { Stage = DriverOperatingStage.Pilot, StageStartedAt = originalPilot, PilotStartedAt = originalPilot,
            ActiveStop = null, Revision = state.Revision + 1, Approvals = state.Approvals.Add(recovery.Approval),
            Recoveries = recoveries, SafetyStoppedAt = null };
    }

    public static DriverOperatingState? Promote(DriverOperatingState state, OperatingApproval approval, DateTimeOffset now)
    {
        var minimum = state.Stage switch
        {
            DriverOperatingStage.Pilot => TimeSpan.FromDays(14),
            DriverOperatingStage.Alpha or DriverOperatingStage.Beta => TimeSpan.FromDays(7),
            DriverOperatingStage.Smoke => TimeSpan.Zero,
            _ => TimeSpan.MaxValue,
        };
        if (!Valid(state, now) || state.ActiveStop is not null || (int)approval.Stage != (int)state.Stage + 1
            || !ApprovalValid(approval, now) || approval.At < state.StageStartedAt
            || approval.At - state.StageStartedAt < minimum || approval.ScopeVersion != state.Manifest.ScopeVersion) return null;
        return state with { Stage = approval.Stage, StageStartedAt = approval.At,
            PilotStartedAt = approval.Stage == DriverOperatingStage.Pilot ? approval.At : state.PilotStartedAt,
            Revision = state.Revision + 1, Approvals = state.Approvals.Add(approval) };
    }

    public static bool Allows(DriverOperatingState state, OperatingRequest request, DateTimeOffset now)
    {
        if (!Valid(state, now) || state.ActiveStop is not null || request.Provenance != state.Manifest.Provenance
            || !Enum.IsDefined(request.Audience)
            || !state.Manifest.Scopes.Contains(new(request.CatalogId, request.CollectionScope, request.Work, request.CatalogRevision))) return false;
        if (request.Work == OperatingWork.BackgroundCollection) return true;
        if (request.UserId is null)
            return state.Stage == DriverOperatingStage.Standard && request.Work == OperatingWork.Publication
                && request.Audience == OperatingAudience.Visitor;
        if (state.Stage is DriverOperatingStage.Smoke or DriverOperatingStage.Pilot)
            return request.UserId is { } user && state.Manifest.Allowlist.Contains(user)
                && state.OptIns.Contains(new(user, state.Manifest.ScopeVersion));
        return request.UserId is { } id && state.OptIns.Contains(new(id, state.Manifest.ScopeVersion))
            && state.Stage switch
            {
                DriverOperatingStage.Alpha => request.Audience >= OperatingAudience.Alpha,
                DriverOperatingStage.Beta => request.Audience >= OperatingAudience.Beta,
                DriverOperatingStage.Standard => request.Audience >= OperatingAudience.Standard,
                _ => false,
            };
    }

    public static bool Valid(DriverOperatingState state, DateTimeOffset now)
    {
        if (state.Manifest is not { } m) return false;
        return m.Provenance == DataProvenance.Demo && !string.IsNullOrWhiteSpace(m.Id) && m.ScopeVersion > 0
            && state.Revision > 0 && Enum.IsDefined(state.Stage) && state.StageStartedAt <= now && state.PilotStartedAt <= now
            && !m.Scopes.IsDefaultOrEmpty && m.Scopes.Distinct().Count() == m.Scopes.Length
            && m.Scopes.All(s => !string.IsNullOrWhiteSpace(s.CatalogId) && !string.IsNullOrWhiteSpace(s.CollectionScope) && Enum.IsDefined(s.Work) && s.CatalogRevision > 0)
            && !m.Allowlist.IsDefaultOrEmpty && m.Allowlist.Length <= 10 && !m.Allowlist.Contains(Guid.Empty)
            && m.Allowlist.Distinct().Count() == m.Allowlist.Length
            && m.Budget is { AcquisitionUnits: > 0, PublicationUnits: > 0, Provenance: DataProvenance.Demo, Verified: true } budget
            && budget.Window > TimeSpan.Zero && !string.IsNullOrWhiteSpace(budget.EvidenceId)
            && !state.OptIns.IsDefault && !state.Approvals.IsDefaultOrEmpty && !state.Stops.IsDefault
            && state.Approvals.Any(a => a.Stage == state.Stage && a.ScopeVersion == m.ScopeVersion && ApprovalValid(a, now));
    }

    private static bool ApprovalValid(OperatingApproval approval, DateTimeOffset now) => Enum.IsDefined(approval.Stage)
        && approval.At <= now && !string.IsNullOrWhiteSpace(approval.EvidenceId) && approval.CompositionReviewed
        && approval.CapacityReviewed && approval.PrerequisitesVerified
        && (approval.Stage != DriverOperatingStage.Standard || approval.VisitorOutputVerified);
}
