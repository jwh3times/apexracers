using System.Text;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Data;

/// <summary>PostgreSQL serializes admission, grants, closure and tracked writes together.
/// The transaction lock is coordination, never proof that a network writer has ended.</summary>
public sealed class DriverAuthorityStore(AppDbContext db, TimeProvider timeProvider)
{
    private const long CoordinationKey = 370;

    public async Task<DriverAuthorizationGrant?> FindGrantAsync(DriverScope scope, CancellationToken ct = default) =>
        await db.Set<DriverAuthorizationGrant>().AsNoTracking().SingleOrDefaultAsync(g =>
            g.UserId == scope.UserId && g.CustomerId == scope.CustomerId && g.Provenance == scope.Provenance, ct);

    public async Task<bool> HasAssociationAsync(Guid userId, CancellationToken ct = default) =>
        await db.Set<DriverAuthorizationGrant>().AnyAsync(g => g.UserId == userId && g.BindingActive, ct);

    /// <summary>Identity profile mutations and verified grants share one primary serialization boundary.
    /// The callback receives the current User and association state while that boundary remains held.</summary>
    public async Task<T> UpdateUnverifiedProfileAsync<T>(Guid userId,
        Func<ApplicationUser, bool, CancellationToken, Task<T>> update, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(ct);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new InvalidOperationException("User not found.");
        await db.Entry(user).ReloadAsync(ct);
        var associated = await HasAssociationAsync(userId, ct);
        var result = await update(user, associated, ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<DriverAccess?> ResolveAsync(DriverScope scope, DriverConsentScope purpose, CancellationToken ct = default)
    {
        var grant = await FindGrantAsync(scope, ct);
        return grant is not null && IsAuthorized(grant, purpose) && await db.Users.AnyAsync(u => u.Id == scope.UserId, ct)
            ? Access(grant, purpose) : null;
    }

    public Task<DriverAccess> GrantAsync(VerifiedDriverProof proof, DriverConsent consent,
        IDriverEnforcementJournal journal, CancellationToken ct = default) => GrantCoreAsync(proof, consent, journal, true, ct);

    public Task<DriverAccess> GrantFreshCollectionAsync(VerifiedDriverProof proof, DriverConsent consent,
        IDriverEnforcementJournal journal, CancellationToken ct = default) => GrantCoreAsync(proof, consent, journal, false, ct);

    private async Task<DriverAccess> GrantCoreAsync(VerifiedDriverProof proof, DriverConsent consent,
        IDriverEnforcementJournal journal, bool restoreDormant, CancellationToken ct)
    {
        ValidateSyntheticProof(proof);
        proof = proof with { VerifiedAt = DriverAuthorizationPolicy.DurableTime(proof.VerifiedAt) };
        if (proof.VerifiedAt > timeProvider.GetUtcNow()) throw new ArgumentException("Ownership proof cannot be from a future time.");
        if (consent.PersonalVersion != DriverAuthorizationPolicy.PersonalConsentVersion
            || consent.SharingVersion is not null && consent.SharingVersion != DriverAuthorizationPolicy.SharingConsentVersion)
            throw new ArgumentException("The current consent versions must be affirmatively accepted.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(ct);
        var enforcement = await journal.ReadCurrentAsync(proof.Scope, ct);
        if (!enforcement.Available || enforcement.PendingIntents.Count != 0)
            throw new InvalidOperationException("Driver enforcement is unavailable.");
        if (!await db.Users.AnyAsync(u => u.Id == proof.Scope.UserId, ct))
            throw new InvalidOperationException("Driver authorization is unavailable.");
        if (await db.Set<DriverLifecycleOperation>().AnyAsync(o => o.Kind == DriverLifecycleKind.DeleteUser
            && db.Set<DriverAuthorizationGrant>().Any(g => g.Id == o.GrantId && g.UserId == proof.Scope.UserId), ct))
            throw new InvalidOperationException("Deleted User authorization cannot be reactivated.");
        // An unverified account claim is deliberately not read here. Historical verified ownership
        // is never silently transferred, even if its current binding is inactive.
        if (await db.Set<DriverAuthorizationGrant>().AnyAsync(g => g.Provenance == proof.Scope.Provenance
            && (g.CustomerId == proof.Scope.CustomerId && g.UserId != proof.Scope.UserId
                || g.UserId == proof.Scope.UserId && g.CustomerId != proof.Scope.CustomerId && g.BindingActive), ct))
            throw new InvalidOperationException("Driver authorization requires explicit conflict resolution.");
        var grant = await LoadGrantAsync(proof.Scope, ct);
        if (grant is not null && grant.Revision < enforcement.MinimumRevision)
            throw new InvalidOperationException("Driver enforcement requires reconciliation.");
        if (grant?.SharingConsentVersion is not null && consent.SharingVersion is null)
            throw new InvalidOperationException("Consent withdrawal requires a durable lifecycle operation.");
        var changesGeneration = grant is not null && (grant.ProofReceiptId != proof.ReceiptId
            || grant.PersonalConsentVersion != consent.PersonalVersion || grant.SharingConsentVersion != consent.SharingVersion
            || grant.AuthorizedDriverName != proof.DriverName || !grant.ProofValid || !grant.BindingActive);
        if (journal is IPublicationHistoryAuthority independent)
        {
            var history = await independent.ReadAsync(ct);
            if (!history.Available || (grant is null || changesGeneration) && history.Releases.Any(r => !r.Terminal
                && r.Proposal.Dependencies.Any(d => d.Scope.CustomerId == proof.Scope.CustomerId && d.Scope.Provenance == proof.Scope.Provenance)))
                throw new InvalidOperationException("Publication history requires reconciliation or writer drain.");
        }
        if ((grant is null || changesGeneration) && await db.Set<PublicationRelease>().AnyAsync(r => r.TerminalAt == null
            && r.Dependencies.Any(d => d.CustomerId == proof.Scope.CustomerId && d.Provenance == proof.Scope.Provenance), ct))
            throw new InvalidOperationException("Authorization generation change requires writer drain.");
        if (changesGeneration && await db.Set<DriverPublicationAdmission>().AnyAsync(a => a.GrantId == grant!.Id && a.TerminalAt == null, ct))
            throw new InvalidOperationException("Authorization generation change requires writer drain.");
        if (restoreDormant && grant?.PersonalClosedAt is { } lossAt && !IsAuthorized(grant, DriverConsentScope.Personal)
            && timeProvider.GetUtcNow() >= lossAt.AddDays(90))
            throw new InvalidOperationException("The retained association is no longer eligible for reactivation.");
        if (grant is not null && (!grant.BindingActive || !grant.ProofValid || grant.PersonalConsentVersion is null
            || grant.SharingConsentVersion is null && consent.SharingVersion is not null))
        {
            if (proof.ReceiptId == grant.ProofReceiptId
                || grant.PersonalClosedAt is { } personalLoss && proof.VerifiedAt <= personalLoss
                || grant.SharingClosedAt is { } sharingLoss && consent.SharingVersion is not null && proof.VerifiedAt <= sharingLoss)
                throw new InvalidOperationException("Fresh ownership proof is required for this authorization generation.");
        }
        var receipt = await db.Set<DriverProofReceipt>().SingleOrDefaultAsync(r => r.Id == proof.ReceiptId, ct);
        if (receipt is not null && (receipt.UserId != proof.Scope.UserId || receipt.CustomerId != proof.Scope.CustomerId
            || receipt.Provenance != proof.Scope.Provenance || receipt.VerifiedAt != proof.VerifiedAt || receipt.Authority != proof.Authority))
            throw new InvalidOperationException("Ownership proof does not match its original binding.");
        if (receipt is null)
        {
            db.Add(new DriverProofReceipt
            {
                Id = proof.ReceiptId,
                UserId = proof.Scope.UserId,
                CustomerId = proof.Scope.CustomerId,
                Provenance = proof.Scope.Provenance,
                VerifiedAt = proof.VerifiedAt,
                Authority = proof.Authority
            });
            await db.SaveChangesAsync(ct);
        }
        if (grant is null)
        {
            grant = new DriverAuthorizationGrant
            {
                Id = Guid.NewGuid(),
                UserId = proof.Scope.UserId,
                CustomerId = proof.Scope.CustomerId,
                Provenance = proof.Scope.Provenance
            };
            db.Add(grant);
        }
        else if (changesGeneration)
            grant.Revision++;
        grant.BindingActive = true;
        grant.ProofValid = true;
        grant.ProofReceiptId = proof.ReceiptId;
        grant.PersonalConsentVersion = consent.PersonalVersion;
        grant.SharingConsentVersion = consent.SharingVersion;
        grant.AuthorizedDriverName = proof.DriverName;
        // A new authorized generation has its own future loss clock. Durable work for prior
        // generations retains its original clock and can never be renewed by this grant.
        grant.PersonalClosedAt = null;
        if (consent.SharingVersion is not null) grant.SharingClosedAt = null;
        if (changesGeneration) await new EvidenceCopyLifecycle(db, timeProvider).RefreshGrantGenerationAsync(grant, ct);
        if (changesGeneration)
        {
            await db.SaveChangesAsync(ct); // New grant generation must be visible to relational writer fences.
            if (restoreDormant) await PrivateUploadStore.RestoreAsync(db, grant, timeProvider, ct);
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Access(grant, DriverConsentScope.Personal);
    }

    public async Task<DriverLifecycleOperation> ApplyIntentAsync(DriverLifecycleIntent intent, CancellationToken ct = default)
    {
        if (intent.OperationId == Guid.Empty || intent.GrantId == Guid.Empty || !Enum.IsDefined(intent.Kind)
            || intent.OriginalLossAt == default)
            throw new ArgumentException("A durable lifecycle intent is required.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(ct);
        var operation = await ApplyAssociationIntentAsync(intent, ct);
        if (intent.Kind == DriverLifecycleKind.DeleteUser)
        {
            // The independent User-wide veto is already durable. Closure and copy work for every
            // association commit together; retries retain deterministic identities and original clocks.
            var associations = await db.DriverAuthorizationGrants.AsNoTracking()
                .Where(g => g.UserId == intent.Scope.UserId && g.Id != intent.GrantId).ToListAsync(ct);
            foreach (var association in associations)
                await ApplyAssociationIntentAsync(intent with
                {
                    OperationId = DriverEnforcement.AssociationOperationId(intent.OperationId, association.Id),
                    GrantId = association.Id,
                    Scope = new(association.UserId, association.CustomerId, association.Provenance)
                }, ct);
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return operation;
    }

    private async Task<DriverLifecycleOperation> ApplyAssociationIntentAsync(DriverLifecycleIntent intent, CancellationToken ct)
    {
        var existing = await db.Set<DriverLifecycleOperation>().AsNoTracking().SingleOrDefaultAsync(o => o.Id == intent.OperationId, ct);
        if (existing is not null)
        {
            if (existing.GrantId != intent.GrantId || existing.Kind != intent.Kind || existing.OriginalLossAt != intent.OriginalLossAt)
                throw new InvalidOperationException("Lifecycle operation does not match its original intent.");
            var current = await LoadGrantAsync(intent.Scope, ct);
            if (current is null || current.Id != intent.GrantId || current.Revision < existing.AppliedRevision
                || current.Revision == existing.AppliedRevision
                    && (current.SharingConsentVersion is not null
                        || intent.Kind != DriverLifecycleKind.WithdrawSharing && current.PersonalConsentVersion is not null))
                throw new InvalidOperationException("Primary enforcement requires reconciliation.");
            return existing;
        }
        var grant = await LoadGrantAsync(intent.Scope, ct)
            ?? throw new InvalidOperationException("Driver authorization is unavailable.");
        if (grant.Id != intent.GrantId)
            throw new InvalidOperationException("Lifecycle operation does not match its original association.");
        grant.Revision++;
        grant.SharingConsentVersion = null;
        grant.SharingClosedAt = Earliest(grant.SharingClosedAt, intent.OriginalLossAt);
        if (intent.Kind != DriverLifecycleKind.WithdrawSharing)
        {
            grant.PersonalConsentVersion = null;
            grant.AuthorizedDriverName = null;
            grant.PersonalClosedAt = Earliest(grant.PersonalClosedAt, intent.OriginalLossAt);
            if (intent.Kind is DriverLifecycleKind.RevokeProof or DriverLifecycleKind.Unlink or DriverLifecycleKind.DeleteUser)
                grant.ProofValid = false;
            if (intent.Kind is DriverLifecycleKind.Unlink or DriverLifecycleKind.DeleteUser)
                grant.BindingActive = false;
        }
        var operation = new DriverLifecycleOperation
        {
            Id = intent.OperationId,
            GrantId = grant.Id,
            Kind = intent.Kind,
            OriginalLossAt = intent.OriginalLossAt,
            AppliedRevision = grant.Revision,
            PrimaryAppliedAt = timeProvider.GetUtcNow()
        };
        db.Add(operation);
        await new EvidenceCopyLifecycle(db, timeProvider).ApplyGrantLossAsync(grant, intent, ct);
        foreach (var purpose in Enum.GetValues<DriverConsentScope>().Where(p => DriverAuthorizationPolicy.Affects(intent.Kind, p)))
        {
            // The bounded prototype payload is opaque and may include an authorized name.
            // It is not the typed private-upload store (#372), so its ceiling is always 24 hours.
            var dueAt = EvidenceRetention.Earliest(DriverAuthorizationPolicy.CleanupDueAt(intent.Kind, purpose, intent.OriginalLossAt),
                EvidenceRetention.NameRemovalDueAt(intent.OriginalLossAt));
            db.Add(new DriverCopyCleanup
            {
                Id = Guid.NewGuid(),
                OperationId = operation.Id,
                GrantId = grant.Id,
                Purpose = purpose,
                ThroughRevision = grant.Revision - 1,
                OriginalLossAt = intent.OriginalLossAt,
                DueAt = dueAt
            });
            await db.Set<DriverTrackedCopy>().Where(c => c.GrantId == grant.Id && c.Purpose == purpose && c.UnavailableAt == null)
                .ExecuteUpdateAsync(update => update.SetProperty(c => c.UnavailableAt, intent.OriginalLossAt), ct);
        }
        await db.SaveChangesAsync(ct);
        return operation;
    }

    public async Task<DriverPublicationAdmission?> AdmitAsync(DriverAccess access, Guid incarnation,
        IDriverEnforcementJournal journal, CancellationToken ct = default)
    {
        if (incarnation == Guid.Empty) throw new ArgumentException("A host incarnation is required.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(ct);
        var enforcement = await journal.ReadCurrentAsync(access.Scope, ct);
        if (!enforcement.Allows(access.Revision, access.Purpose)) return null;
        var grant = await LoadGrantAsync(access.Scope, ct);
        if (grant is null || grant.Id != access.GrantId || grant.Revision != access.Revision || !IsAuthorized(grant, access.Purpose)
            || !await db.Users.AnyAsync(u => u.Id == access.Scope.UserId, ct))
            return null;
        var now = timeProvider.GetUtcNow();
        var admission = new DriverPublicationAdmission
        {
            Id = Guid.NewGuid(),
            GrantId = grant.Id,
            Revision = grant.Revision,
            Purpose = access.Purpose,
            Incarnation = incarnation,
            AdmittedAt = now,
            LeaseUntil = now.AddMinutes(1)
        };
        db.Add(admission);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return admission;
    }

    /// <summary>Call only after the owned executor can make no further application writes.</summary>
    public async Task CheckpointAsync(Guid admissionId, Guid incarnation, CancellationToken ct = default)
    {
        var rows = await db.Set<DriverPublicationAdmission>().Where(a => a.Id == admissionId && a.Incarnation == incarnation)
            .ExecuteUpdateAsync(update => update.SetProperty(a => a.TerminalAt, timeProvider.GetUtcNow()), ct);
        if (rows != 1) throw new InvalidOperationException("The checkpoint does not match this writer incarnation.");
    }

    public Task<int> PendingWritersAsync(DriverLifecycleIntent intent, CancellationToken ct = default) => PendingWritersAsync(intent, null, ct);

    public async Task<int> PendingWritersAsync(DriverLifecycleIntent intent, IDriverEnforcementJournal? journal, CancellationToken ct = default)
    {
        var cohortIds = (await db.Set<PublicationRelease>().Where(r => r.TerminalAt == null
            && (intent.Kind == DriverLifecycleKind.DeleteUser && r.RecipientUserId == intent.Scope.UserId
                || r.Dependencies.Any(d => intent.Kind == DriverLifecycleKind.DeleteUser ? d.UserId == intent.Scope.UserId
                    : d.CustomerId == intent.Scope.CustomerId && d.Provenance == intent.Scope.Provenance))).Select(r => r.Id).ToListAsync(ct)).ToHashSet();
        var unknownHistory = 0;
        if (journal is IPublicationHistoryAuthority independent)
        {
            var history = await independent.ReadAsync(ct);
            if (!history.Available) unknownHistory = 1;
            else foreach (var r in history.Releases.Where(r => !r.Terminal && (intent.Kind == DriverLifecycleKind.DeleteUser && r.Proposal.RecipientUserId == intent.Scope.UserId
                || r.Proposal.Dependencies.Any(d => intent.Kind == DriverLifecycleKind.DeleteUser ? d.Scope.UserId == intent.Scope.UserId
                    : d.Scope.CustomerId == intent.Scope.CustomerId && d.Scope.Provenance == intent.Scope.Provenance)))) cohortIds.Add(r.Proposal.Id);
        }
        var cohortWriters = cohortIds.Count + unknownHistory;
        if (intent.Kind == DriverLifecycleKind.DeleteUser)
            return cohortWriters + await db.DriverPublicationAdmissions.CountAsync(a => a.TerminalAt == null
                && db.DriverAuthorizationGrants.Any(g => g.Id == a.GrantId && g.UserId == intent.Scope.UserId), ct);
        var revision = await db.Set<DriverLifecycleOperation>().Where(o => o.Id == intent.OperationId && o.GrantId == intent.GrantId)
            .Select(o => o.AppliedRevision).SingleAsync(ct);
        return cohortWriters + await db.Set<DriverPublicationAdmission>().CountAsync(a => a.GrantId == intent.GrantId
            && a.Revision < revision && a.TerminalAt == null
            && (intent.Kind != DriverLifecycleKind.WithdrawSharing || a.Purpose == DriverConsentScope.Sharing), ct);
    }

    public async Task CompleteOperationAsync(DriverLifecycleIntent intent, CancellationToken ct = default)
    {
        if (intent.Kind == DriverLifecycleKind.DeleteUser)
        {
            var grants = await db.DriverAuthorizationGrants.AsNoTracking().Where(g => g.UserId == intent.Scope.UserId)
                .Select(g => g.Id).ToListAsync(ct);
            var ids = grants.Select(id => id == intent.GrantId ? intent.OperationId
                : DriverEnforcement.AssociationOperationId(intent.OperationId, id)).ToArray();
            await db.DriverLifecycleOperations.Where(o => ids.Contains(o.Id))
                .ExecuteUpdateAsync(update => update.SetProperty(o => o.CompletedAt, timeProvider.GetUtcNow()), ct);
            return;
        }
        var rows = await db.Set<DriverLifecycleOperation>().Where(o => o.Id == intent.OperationId && o.GrantId == intent.GrantId)
            .ExecuteUpdateAsync(update => update.SetProperty(o => o.CompletedAt, timeProvider.GetUtcNow()), ct);
        if (rows != 1) throw new InvalidOperationException("Lifecycle completion has no matching primary operation.");
    }

    public async Task<Guid> CommitCopyAsync(DriverAccess access, string payload,
        IDriverEnforcementJournal journal, CancellationToken ct = default)
    {
        if (Encoding.UTF8.GetByteCount(payload) > 16_384) throw new ArgumentException("The tracked copy exceeds its bounded size.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(ct);
        var enforcement = await journal.ReadCurrentAsync(access.Scope, ct);
        if (!enforcement.Allows(access.Revision, access.Purpose))
            throw new InvalidOperationException("Driver enforcement is unavailable.");
        var grant = await LoadGrantAsync(access.Scope, ct);
        if (grant is null || grant.Id != access.GrantId || grant.Revision != access.Revision || !IsAuthorized(grant, access.Purpose)
            || !await db.Users.AnyAsync(u => u.Id == access.Scope.UserId, ct))
            throw new InvalidOperationException("Driver authorization is unavailable.");
        var copy = new DriverTrackedCopy
        {
            Id = Guid.NewGuid(),
            GrantId = grant.Id,
            Revision = grant.Revision,
            Purpose = access.Purpose,
            Payload = payload,
            CreatedAt = timeProvider.GetUtcNow()
        };
        db.Add(copy);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return copy.Id;
    }

    public async Task<int> RemoveDueCopiesAsync(CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(ct);
        var now = timeProvider.GetUtcNow();
        var work = db.Set<DriverCopyCleanup>().Where(w => w.DueAt <= now.AddMinutes(30) && w.VerifiedRemovedAt == null);
        // One correlated delete and one verification update cover every due association. Active
        // copies and later generations remain outside old cleanup even when they share a grant.
        await db.Set<DriverTrackedCopy>().Where(c => c.UnavailableAt != null && work.Any(w =>
            w.GrantId == c.GrantId && w.Purpose == c.Purpose && c.Revision <= w.ThroughRevision)).ExecuteDeleteAsync(ct);
        var completed = await work.Where(w => !db.Set<DriverTrackedCopy>().Any(c =>
            c.GrantId == w.GrantId && c.Purpose == w.Purpose && c.Revision <= w.ThroughRevision && c.UnavailableAt != null))
            .ExecuteUpdateAsync(update => update.SetProperty(w => w.VerifiedRemovedAt, now), ct);
        await transaction.CommitAsync(ct);
        return completed;
    }

    /// <summary>Explanatory proof authority expires after twelve calendar months. The receipt's
    /// minimal binding and original timestamp remain as enforcement history, without a profile/name.</summary>
    public async Task<int> RemoveExpiredExplanationsAsync(CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(ct);
        var cutoff = timeProvider.GetUtcNow().AddMonths(-12).AddMinutes(30);
        var removed = await db.DriverProofReceipts.Where(r => r.VerifiedAt <= cutoff && r.Authority != string.Empty)
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.Authority, string.Empty), ct);
        await transaction.CommitAsync(ct);
        return removed;
    }

    private async Task LockAsync(CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) throw new InvalidOperationException("Driver authority requires PostgreSQL coordination.");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({CoordinationKey})", ct);
    }

    private async Task<DriverAuthorizationGrant?> LoadGrantAsync(DriverScope scope, CancellationToken ct)
    {
        var grant = await db.Set<DriverAuthorizationGrant>().SingleOrDefaultAsync(g =>
            g.UserId == scope.UserId && g.CustomerId == scope.CustomerId && g.Provenance == scope.Provenance, ct);
        if (grant is not null) await db.Entry(grant).ReloadAsync(ct);
        return grant;
    }

    private static bool IsAuthorized(DriverAuthorizationGrant grant, DriverConsentScope purpose) =>
        grant.Provenance == DataProvenance.Demo && grant.BindingActive && grant.ProofValid
        && grant.PersonalConsentVersion == DriverAuthorizationPolicy.PersonalConsentVersion
        && (purpose == DriverConsentScope.Personal || purpose == DriverConsentScope.Sharing
            && grant.SharingConsentVersion == DriverAuthorizationPolicy.SharingConsentVersion);
    private static DriverAccess Access(DriverAuthorizationGrant grant, DriverConsentScope purpose) =>
        new(new(grant.UserId, grant.CustomerId, grant.Provenance), grant.Id, grant.Revision, purpose, grant.AuthorizedDriverName);
    private static DateTimeOffset Earliest(DateTimeOffset? previous, DateTimeOffset incoming) => previous < incoming ? previous.Value : incoming;
    private static void ValidateSyntheticProof(VerifiedDriverProof proof)
    {
        if (proof.Scope.Provenance != DataProvenance.Demo || proof.Scope.UserId == Guid.Empty || proof.Scope.CustomerId <= 0
            || proof.ReceiptId == Guid.Empty || proof.VerifiedAt == default || string.IsNullOrWhiteSpace(proof.Authority)
            || proof.Authority.Length > 128 || string.IsNullOrWhiteSpace(proof.DriverName) || proof.DriverName.Length > 200)
            throw new InvalidOperationException("Verified Driver ownership is unavailable.");
    }
}
