using System.Collections.Immutable;
using System.Text.Json;
using System.Security.Cryptography;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Data;

/// <summary>One global accounting domain, serialized with grants, evidence and closure. No catalog,
/// recipient or namespace can select a fresh history. Independent intent precedes primary commit.</summary>
public sealed class PublicationReleaseStore(AppDbContext db, TimeProvider clock,
    IPublicationHistoryAuthority history, IPublicationCompositionReview review, Guid trustedEpoch)
{
    public async Task<ImmutableArray<PublicationDependency>?> ObserveAsync(
        ImmutableArray<DriverScope> scopes, IReadOnlyDictionary<int, DriverConsentScope> required,
        IDriverEnforcementJournal journal, CancellationToken ct = default)
    {
        if (scopes.IsDefaultOrEmpty || scopes.Any(s => s.Provenance != DataProvenance.Demo || s.CustomerId <= 0)
            || scopes.Select(s => (s.CustomerId, s.Provenance)).Distinct().Count() != scopes.Length) return null;
        var result = ImmutableArray.CreateBuilder<PublicationDependency>();
        foreach (var scope in scopes.OrderBy(s => s.CustomerId))
        {
            // Absence is Customer/provenance-wide. Another User's later Sharing grant matters.
            var grant = await db.DriverAuthorizationGrants.AsNoTracking().SingleOrDefaultAsync(g =>
                g.CustomerId == scope.CustomerId && g.Provenance == scope.Provenance, ct);
            var actual = scope with { UserId = grant?.UserId ?? Guid.Empty };
            DriverConsentScope? purpose = required.TryGetValue(scope.CustomerId, out var value) ? value : null;
            if (purpose is not null && (grant is null || scope.UserId != grant.UserId
                || await new DriverAuthorityStore(db, clock).ResolveAsync(actual, purpose.Value, ct) is null)) return null;
            var revision = grant?.Revision ?? 0;
            var enforcement = await journal.ReadCurrentAsync(actual, ct);
            if (!enforcement.Allows(revision, purpose ?? DriverConsentScope.Sharing)) return null;
            var authorityHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(grant is null ? null : new
            {
                grant.Id,
                grant.UserId,
                grant.CustomerId,
                grant.Provenance,
                grant.Revision,
                grant.BindingActive,
                grant.ProofValid,
                grant.ProofReceiptId,
                grant.PersonalConsentVersion,
                grant.SharingConsentVersion,
                grant.AuthorizedDriverName,
                grant.PersonalClosedAt,
                grant.SharingClosedAt
            })));
            result.Add(new(actual, grant?.Id, revision, purpose, authorityHash));
        }
        return result.ToImmutable();
    }

    public async Task<PublicationAccounting?> ReserveAsync(PublicationProposal proposal,
        Func<CancellationToken, Task<bool>> sourceCurrent, IDriverEnforcementJournal journal, CancellationToken ct = default)
    {
        if (!Valid(proposal) || !ReferenceEquals(history, journal)) return null;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(ct);
        if (!await RecipientCurrentAsync(proposal, journal, ct) || !await sourceCurrent(ct) || !await DependenciesCurrentAsync(proposal.Dependencies, journal, ct)) return null;
        var current = await history.ReadAsync(ct);
        if (!await MatchesAsync(current, ct) || !await review.AssessAsync(proposal, current.Releases, ct)) return null;
        // An uncertain external append or primary commit is possible disclosure until its owned
        // executor proves non-dispatch. Missing primary rows never erase this independent intent.
        var reserved = await history.ReserveAsync(proposal, ct);
        if (JsonSerializer.Serialize(reserved.Proposal) != JsonSerializer.Serialize(proposal) || reserved.Sequence != current.Releases.Length + 1
            || reserved.DispatchStarted || reserved.Terminal || reserved.ProvenUnsent) throw new EvidenceCopyUnavailableException();
        db.Add(Row(reserved));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return reserved;
    }

    public async Task<bool> BeginDispatchAsync(PublicationAccounting admission,
        Func<CancellationToken, Task<bool>> sourceCurrent, IDriverEnforcementJournal journal, CancellationToken ct = default)
    {
        if (!ReferenceEquals(history, journal)) return false;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(ct);
        var row = await db.Set<PublicationRelease>().Include(r => r.Dependencies).SingleOrDefaultAsync(r => r.Id == admission.Proposal.Id, ct);
        if (row is null || row.Incarnation != admission.Proposal.Incarnation || row.TerminalAt is not null
            || row.DispatchStartedAt is not null || !await RecipientCurrentAsync(admission.Proposal, journal, ct) || !await sourceCurrent(ct)
            || !await DependenciesCurrentAsync(admission.Proposal.Dependencies, journal, ct)
            || !await MatchesAsync(await history.ReadAsync(ct), ct)) return false;
        // Persist conservative possible dispatch on the independent side before any owned write.
        await history.MarkDispatchAsync(row.Id, row.Incarnation, ct);
        var afterMark = await history.ReadAsync(ct);
        var started = afterMark.Releases.SingleOrDefault(r => r.Proposal.Id == row.Id);
        if (!Complete(afterMark) || started is null || !IdentityMatches(row, started) || started.Terminal || !started.DispatchStarted)
            throw new EvidenceCopyUnavailableException();
        row.DispatchStartedAt = started.DispatchStartedAt;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    /// <summary>Only the module-owned ended executor calls this. Cancellation/lease/session loss
    /// is never terminality, and a write attempt can never be classified as proven unsent.</summary>
    public async Task CheckpointAsync(PublicationAccounting admission, bool provenUnsent, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(ct);
        var row = await db.Set<PublicationRelease>().Include(r => r.Dependencies).SingleOrDefaultAsync(r => r.Id == admission.Proposal.Id, ct)
            ?? throw new EvidenceCopyUnavailableException();
        if (row.Incarnation != admission.Proposal.Incarnation || provenUnsent && row.DispatchStartedAt is not null
            || row.TerminalAt is not null && row.ProvenUnsent != provenUnsent)
            throw new EvidenceCopyUnavailableException();
        var external = await history.ReadAsync(ct);
        var known = external.Releases.SingleOrDefault(r => r.Proposal.Id == row.Id);
        if (!Complete(external) || known is null || !IdentityMatches(row, known)
            || provenUnsent && known.DispatchStarted || known.Terminal && known.ProvenUnsent != provenUnsent) throw new EvidenceCopyUnavailableException();
        await history.CheckpointAsync(row.Id, row.Incarnation, provenUnsent, ct);
        var afterCheckpoint = await history.ReadAsync(ct);
        var checkpoint = afterCheckpoint.Releases.SingleOrDefault(r => r.Proposal.Id == row.Id);
        if (!Complete(afterCheckpoint) || checkpoint is null || !IdentityMatches(row, checkpoint)
            || !checkpoint.Terminal || checkpoint.ProvenUnsent != provenUnsent) throw new EvidenceCopyUnavailableException();
        row.TerminalAt = checkpoint.TerminalAt;
        row.ProvenUnsent = provenUnsent;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>Explicit recovery imports the independently current minimal history; it cannot
    /// infer dispatch absence or terminality from primary restore, process expiry or missing rows.</summary>
    public async Task<bool> ReconcileHistoryAsync(CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(ct);
        var current = await history.ReadAsync(ct);
        if (!Complete(current)) return false;
        var rows = await db.Set<PublicationRelease>().Include(r => r.Dependencies).ToListAsync(ct);
        if (rows.Any(r => !current.Releases.Any(e => e.Proposal.Id == r.Id && IdentityMatches(r, e)))) return false;
        foreach (var entry in current.Releases)
        {
            var row = rows.SingleOrDefault(r => r.Id == entry.Proposal.Id);
            if (row is null) db.Add(Row(entry));
            else
            {
                if (row.TerminalAt is not null && row.TerminalAt != entry.TerminalAt
                    || row.DispatchStartedAt is not null && row.DispatchStartedAt != entry.DispatchStartedAt) return false;
                // A primary-only checkpoint cannot outweigh the independent possible history.
                row.DispatchStartedAt = entry.DispatchStartedAt;
                row.TerminalAt = entry.TerminalAt;
                row.ProvenUnsent = entry.ProvenUnsent;
            }
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    private async Task<bool> DependenciesCurrentAsync(ImmutableArray<PublicationDependency> dependencies,
        IDriverEnforcementJournal journal, CancellationToken ct)
    {
        var required = dependencies.Where(d => d.RequiredPurpose is not null).ToDictionary(d => d.Scope.CustomerId, d => d.RequiredPurpose!.Value);
        var observed = await ObserveAsync(dependencies.Select(d => d.Scope).ToImmutableArray(), required, journal, ct);
        return observed is not null && observed.Value.SequenceEqual(dependencies);
    }

    public async Task<bool> NamesMatchAsync(IReadOnlyDictionary<int, string> names, CancellationToken ct = default)
    {
        foreach (var name in names)
            if (!await db.DriverAuthorizationGrants.AnyAsync(g => g.CustomerId == name.Key && g.Provenance == DataProvenance.Demo
                && g.AuthorizedDriverName == name.Value && g.SharingConsentVersion == DriverAuthorizationPolicy.SharingConsentVersion, ct)) return false;
        return true;
    }

    private async Task<bool> RecipientCurrentAsync(PublicationProposal proposal, IDriverEnforcementJournal journal, CancellationToken ct)
    {
        if (proposal.RecipientUserId is not { } id) return proposal.Purpose == PublicationPurpose.Aggregate;
        var enforcement = await journal.ReadUserAsync(id, ct);
        return enforcement.Available && enforcement.Deletion is null && await db.Users.AnyAsync(u => u.Id == id, ct);
    }

    private bool Complete(PublicationHistory current) => trustedEpoch != Guid.Empty && current.Available && current.Epoch == trustedEpoch
        && !current.Releases.IsDefault && current.Releases.Select(r => r.Sequence).SequenceEqual(Enumerable.Range(1, current.Releases.Length).Select(i => (long)i))
        && current.Releases.Select(r => r.Proposal.Id).Distinct().Count() == current.Releases.Length;

    private async Task<bool> MatchesAsync(PublicationHistory current, CancellationToken ct)
    {
        if (!Complete(current)) return false;
        var rows = await db.Set<PublicationRelease>().AsNoTracking().Include(r => r.Dependencies).ToArrayAsync(ct);
        return rows.Length == current.Releases.Length && rows.All(r => current.Releases.Any(e => IdentityMatches(r, e)
            && r.DispatchStartedAt == e.DispatchStartedAt && r.TerminalAt == e.TerminalAt && r.ProvenUnsent == e.ProvenUnsent));
    }

    private static bool IdentityMatches(PublicationRelease row, PublicationAccounting entry)
    {
        var p = entry.Proposal;
        return row.Id == p.Id && row.Sequence == entry.Sequence && row.ContextHash == p.ContextHash
            && row.RepresentationHash == p.RepresentationHash && row.DependencyHash == p.DependencyHash
            && row.CatalogId == p.CatalogId && row.CatalogRevision == p.CatalogRevision && row.Provenance == p.Provenance
            && row.RecipientUserId == p.RecipientUserId && row.Purpose == p.Purpose && row.Incarnation == p.Incarnation
            && row.ReservedAt == entry.ReservedAt && row.Dependencies.OrderBy(d => d.CustomerId).Select(d =>
                new PublicationDependency(new(d.UserId, d.CustomerId, d.Provenance), d.GrantId, d.Revision, d.RequiredPurpose, d.AuthorityHash)).SequenceEqual(p.Dependencies);
    }

    private static PublicationRelease Row(PublicationAccounting entry)
    {
        var p = entry.Proposal;
        return new()
        {
            Id = p.Id,
            Sequence = entry.Sequence,
            ContextHash = p.ContextHash,
            RepresentationHash = p.RepresentationHash,
            DependencyHash = p.DependencyHash,
            CatalogId = p.CatalogId,
            CatalogRevision = p.CatalogRevision,
            Provenance = p.Provenance,
            RecipientUserId = p.RecipientUserId,
            Purpose = p.Purpose,
            Incarnation = p.Incarnation,
            ReservedAt = entry.ReservedAt,
            DispatchStartedAt = entry.DispatchStartedAt,
            TerminalAt = entry.TerminalAt,
            ProvenUnsent = entry.ProvenUnsent,
            Dependencies = p.Dependencies.Select(d => new PublicationReleaseDependency
            {
                ReleaseId = p.Id,
                UserId = d.Scope.UserId,
                CustomerId = d.Scope.CustomerId,
                Provenance = d.Scope.Provenance,
                GrantId = d.GrantId,
                Revision = d.Revision,
                RequiredPurpose = d.RequiredPurpose,
                AuthorityHash = d.AuthorityHash
            }).ToArray()
        };
    }
    private static bool Valid(PublicationProposal p) => p.Id != Guid.Empty && p.Incarnation != Guid.Empty
        && p.CatalogId is "synthetic-lap-rating-v1" or DriverReferences.CatalogId && p.CatalogRevision > 0 && p.Provenance == DataProvenance.Demo
        && Enum.IsDefined(p.Purpose) && (p.Purpose == PublicationPurpose.Aggregate ? p.RecipientUserId is null : p.RecipientUserId is { } id && id != Guid.Empty)
        && new[] { p.ContextHash, p.DependencyHash, p.RepresentationHash }.All(s => s.Length == 64 && s.All(char.IsAsciiHexDigit))
        && !p.Dependencies.IsDefaultOrEmpty;
    private async Task LockAsync(CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) throw new EvidenceCopyUnavailableException();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({370L})", ct);
    }
}
