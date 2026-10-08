using ApexRacers.Core;
using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Data;

public sealed record DriverReferenceBinding(ScopedDriverReference Reference, DriverAccess Recipient, DriverAccess Target);
public sealed record IssuedDriverReference(string Value, DateTimeOffset ExpiresAt);

/// <summary>Reference and private Follow decisions share the lifecycle/admission lock. Neither an
/// opaque reference nor an old Follow grants access: every decision resolves both current grants.</summary>
public sealed class DriverReferenceStore(AppDbContext db, TimeProvider clock, IDriverEnforcementJournal journal)
{
    public async Task<DriverAccess?> RecipientAsync(Guid userId, CancellationToken ct = default)
    {
        if (db.Provenance != DataProvenance.Demo) return null;
        var grant = await db.DriverAuthorizationGrants.AsNoTracking().SingleOrDefaultAsync(g => g.UserId == userId
            && g.Provenance == DataProvenance.Demo && g.BindingActive, ct);
        return grant is null ? null : await ResolveAsync(grant, DriverConsentScope.Personal, ct);
    }
    public async Task<DriverAccess?> TargetAsync(DriverScope scope, CancellationToken ct = default)
    {
        if (db.Provenance != DataProvenance.Demo || scope.Provenance != db.Provenance) return null;
        var grant = await new DriverAuthorityStore(db, clock).FindGrantAsync(scope, ct);
        return grant is null ? null : await ResolveAsync(grant, DriverConsentScope.Sharing, ct);
    }
    public async Task<IssuedDriverReference?> IssueAsync(Guid userId, DriverScope target, DriverReferencePurpose purpose, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(purpose)) return null;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(ct);
        var recipient = await RecipientAsync(userId, ct);
        var access = await TargetAsync(target, ct);
        if (recipient is null || access is null || recipient.Scope == access.Scope) return null;
        var r = await db.DriverAuthorizationGrants.AsNoTracking().SingleAsync(g => g.Id == recipient.GrantId, ct);
        var t = await db.DriverAuthorizationGrants.AsNoTracking().SingleAsync(g => g.Id == access.GrantId, ct);
        var token = DriverReferences.Create();
        var now = DriverAuthorizationPolicy.DurableTime(clock.GetUtcNow());
        db.Add(new ScopedDriverReference { TokenHash = DriverReferences.Hash(token), RecipientGrantId = r.Id,
            RecipientRevision = r.Revision, RecipientProofId = r.ProofReceiptId, TargetGrantId = t.Id,
            TargetRevision = t.Revision, TargetProofId = t.ProofReceiptId, Purpose = purpose,
            Provenance = DataProvenance.Demo, CreatedAt = now, ExpiresAt = now.Add(DriverReferences.Lifetime) });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(token, now.Add(DriverReferences.Lifetime));
    }
    public async Task<DriverReferenceBinding?> ResolveAsync(Guid userId, string? token, DriverReferencePurpose purpose, CancellationToken ct = default)
    {
        if (!DriverReferences.Valid(token) || !Enum.IsDefined(purpose)) return null;
        var hash = DriverReferences.Hash(token!);
        return await ResolveHashAsync(userId, hash, purpose, ct);
    }
    public async Task<bool> BindingCurrentAsync(Guid userId, DriverReferenceBinding binding, CancellationToken ct = default)
    {
        var current = await ResolveHashAsync(userId, binding.Reference.TokenHash, binding.Reference.Purpose, ct);
        return current is not null && current.Recipient == binding.Recipient && current.Target == binding.Target
            && current.Reference.ExpiresAt == binding.Reference.ExpiresAt;
    }
    private async Task<DriverReferenceBinding?> ResolveHashAsync(Guid userId, string hash, DriverReferencePurpose purpose, CancellationToken ct)
    {
        var reference = await db.Set<ScopedDriverReference>().AsNoTracking().SingleOrDefaultAsync(r => r.TokenHash == hash, ct);
        if (db.Provenance != DataProvenance.Demo || reference is null || reference.Provenance != db.Provenance || reference.Purpose != purpose || reference.Provenance != DataProvenance.Demo
            || reference.ExpiresAt <= clock.GetUtcNow() || reference.CreatedAt > clock.GetUtcNow()) return null;
        var recipient = await db.DriverAuthorizationGrants.AsNoTracking().SingleOrDefaultAsync(g => g.Id == reference.RecipientGrantId, ct);
        var target = await db.DriverAuthorizationGrants.AsNoTracking().SingleOrDefaultAsync(g => g.Id == reference.TargetGrantId, ct);
        if (recipient is null || target is null || recipient.UserId != userId || recipient.Revision != reference.RecipientRevision
            || target.Revision != reference.TargetRevision || recipient.ProofReceiptId != reference.RecipientProofId
            || target.ProofReceiptId != reference.TargetProofId) return null;
        var recipientAccess = await ResolveAsync(recipient, DriverConsentScope.Personal, ct);
        var targetAccess = await ResolveAsync(target, DriverConsentScope.Sharing, ct);
        return recipientAccess is null || targetAccess is null ? null : new(reference, recipientAccess, targetAccess);
    }
    public Task<bool> AddFollowAsync(Guid userId, string token, CancellationToken ct = default) =>
        DriverReferences.Valid(token) ? AddFollowCoreAsync(userId, DriverReferences.Hash(token), null, ct) : Task.FromResult(false);
    public Task<bool> AddFollowAsync(Guid userId, DriverReferenceBinding binding, CancellationToken ct = default) =>
        binding.Reference.Purpose == DriverReferencePurpose.Follow ? AddFollowCoreAsync(userId, binding.Reference.TokenHash, binding, ct) : Task.FromResult(false);
    private async Task<bool> AddFollowCoreAsync(Guid userId, string hash, DriverReferenceBinding? expected, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(ct);
        var binding = await ResolveHashAsync(userId, hash, DriverReferencePurpose.Follow, ct);
        if (binding is null || expected is not null && (expected.Recipient != binding.Recipient || expected.Target != binding.Target)) return false;
        var existing = await db.Set<PrivateDriverFollow>().SingleOrDefaultAsync(f => f.RecipientGrantId == binding.Recipient.GrantId
            && f.TargetGrantId == binding.Target.GrantId, ct);
        if (existing is { Active: false })
        {
            if (existing.ReactivateBefore <= clock.GetUtcNow()) return false;
            existing.Active = true; // Original dormant clocks remain, including after retries.
        }
        else if (existing is null)
            db.Add(new PrivateDriverFollow { Id = Guid.NewGuid(), RecipientGrantId = binding.Recipient.GrantId,
                TargetGrantId = binding.Target.GrantId, Provenance = DataProvenance.Demo,
                CreatedAt = DriverAuthorizationPolicy.DurableTime(clock.GetUtcNow()) });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }
    public async Task<IReadOnlyList<DriverScope>> FollowedAsync(Guid userId, IReadOnlyList<DriverScope> catalogScopes, CancellationToken ct = default)
    {
        if (catalogScopes.Count > 12) return [];
        var recipient = await RecipientAsync(userId, ct);
        if (recipient is null) return [];
        var customerIds = catalogScopes.Select(s => s.CustomerId).ToArray();
        var grants = await db.DriverAuthorizationGrants.AsNoTracking().Where(g => g.Provenance == DataProvenance.Demo
            && customerIds.Contains(g.CustomerId) && db.Set<PrivateDriverFollow>().Any(f => f.RecipientGrantId == recipient.GrantId && f.Active && f.TargetGrantId == g.Id))
            .ToListAsync(ct);
        var userIds = grants.Select(g => g.UserId).ToArray();
        var existingUsers = await db.Users.Where(u => userIds.Contains(u.Id)).Select(u => u.Id).ToListAsync(ct);
        var current = new List<DriverScope>();
        foreach (var grant in grants)
        {
            var scope = new DriverScope(grant.UserId, grant.CustomerId, grant.Provenance);
            if (catalogScopes.Contains(scope) && existingUsers.Contains(grant.UserId)
                && DriverAuthorityStore.IsAuthorized(grant, DriverConsentScope.Sharing)
                && (await journal.ReadCurrentAsync(scope, ct)).Allows(grant.Revision, DriverConsentScope.Sharing)) current.Add(scope);
        }
        return current;
    }
    public async Task<bool> FollowBindingsCurrentAsync(IReadOnlyList<DriverReferenceBinding> bindings, CancellationToken ct = default)
    {
        var recipient = bindings.First().Recipient.GrantId;
        var targets = bindings.Select(b => b.Target.GrantId).Distinct().ToArray();
        var current = await db.Set<PrivateDriverFollow>().AsNoTracking().Where(f => f.RecipientGrantId == recipient && f.Active && targets.Contains(f.TargetGrantId))
            .Select(f => f.TargetGrantId).ToListAsync(ct);
        return current.Count == targets.Length;
    }
    public async Task<int> ReconcileAsync(CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(ct);
        var now = clock.GetUtcNow();
        var removed = await db.Set<ScopedDriverReference>().Where(r => r.ExpiresAt <= now).ExecuteDeleteAsync(ct);
        // Day 90 is deletion-due; remove promptly. The immutable day-97 maximum is retained
        // while reconciliation is interrupted, never renewed by retries.
        removed += await db.Set<PrivateDriverFollow>().Where(f => !f.Active && (f.ReactivateBefore <= now || f.RemoveBy <= now)).ExecuteDeleteAsync(ct);
        await tx.CommitAsync(ct);
        return removed;
    }
    internal static async Task LoseAsync(AppDbContext db, DriverAuthorizationGrant grant, DriverLifecycleIntent intent, CancellationToken ct)
    {
        var affected = await db.Set<PrivateDriverFollow>().Where(f => f.TargetGrantId == grant.Id
            || f.RecipientGrantId == grant.Id && intent.Kind != DriverLifecycleKind.WithdrawSharing).ToListAsync(ct);
        foreach (var follow in affected)
        {
            follow.Active = false;
            follow.OriginalLossAt = follow.OriginalLossAt is { } old && old < intent.OriginalLossAt ? old : intent.OriginalLossAt;
            follow.ReactivateBefore = follow.OriginalLossAt.Value.AddDays(90);
            var due = intent.Kind == DriverLifecycleKind.DeleteUser ? intent.OriginalLossAt.AddDays(7) : follow.OriginalLossAt.Value.AddDays(97);
            follow.RemoveBy = follow.RemoveBy is { } prior && prior < due ? prior : due;
        }
        await db.Set<ScopedDriverReference>().Where(r => r.TargetGrantId == grant.Id || r.RecipientGrantId == grant.Id).ExecuteDeleteAsync(ct);
    }
    private async Task<DriverAccess?> ResolveAsync(DriverAuthorizationGrant grant, DriverConsentScope purpose, CancellationToken ct)
    {
        var scope = new DriverScope(grant.UserId, grant.CustomerId, grant.Provenance);
        var state = await journal.ReadCurrentAsync(scope, ct);
        if (!state.Allows(grant.Revision, purpose)) return null;
        return await new DriverAuthorityStore(db, clock).ResolveAsync(scope, purpose, ct);
    }
    private async Task LockAsync(CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) throw new EvidenceCopyUnavailableException();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({370L})", ct);
    }
}
