using System.Security.Cryptography;
using System.Text;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Data;

/// <summary>Capture before acquisition, commit a closed batch, and reconcile physical copies.
/// PostgreSQL serializes these operations with consent and publication admission (lock 370).
/// Receipts bind original time, purpose generation, key version and concrete retained sources.
/// They are never ownership proof. Real issuance remains unavailable without the later adapters.</summary>
public sealed class EvidenceCopyLifecycle(AppDbContext db, TimeProvider clock, IDriverEnforcementJournal? journal = null)
{
    public static readonly Guid PreviewPurposeId = new("a17dc0de-0371-4000-8000-000000000001");

    /// <summary>The only ordinary issuer today is explicitly synthetic preview collection.
    /// Controlled official/personal scenarios also use Demo provenance; this never approves Real data.</summary>
    public async Task<Guid> OpenSyntheticPurposeAsync(EvidencePurposeKind kind = EvidencePurposeKind.SyntheticPreview,
        int? seasonId = null, DriverAccess? access = null, Guid? historicalRequestId = null, CancellationToken ct = default)
    {
        if (db.Provenance != DataProvenance.Demo || !Enum.IsDefined(kind)
            || kind == EvidencePurposeKind.IndependentOfficial && seasonId is null
            || kind == EvidencePurposeKind.AuthorizedHistory && (seasonId is null || historicalRequestId is null || historicalRequestId == Guid.Empty)
            || kind == EvidencePurposeKind.Sharing && access?.Purpose != DriverConsentScope.Sharing
            || kind is EvidencePurposeKind.Personal or EvidencePurposeKind.Sharing or EvidencePurposeKind.AuthorizedHistory && access is null)
            throw new EvidenceCopyUnavailableException();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(ct);
        if (access is not null) await CheckAccessAsync(access, ct);
        var id = kind == EvidencePurposeKind.SyntheticPreview ? PreviewPurposeId : Guid.NewGuid();
        var existing = kind == EvidencePurposeKind.SyntheticPreview
            ? await db.EvidencePurposes.AsNoTracking().SingleOrDefaultAsync(p => p.Provenance == DataProvenance.Demo
                && p.Kind == EvidencePurposeKind.SyntheticPreview && p.OriginalEndedAt == null, ct)
            : null;
        if (existing is not null)
        {
            if (existing.OriginalEndedAt is not null) throw new EvidenceCopyUnavailableException();
            return existing.Id;
        }
        if (kind == EvidencePurposeKind.SyntheticPreview && await db.EvidencePurposes.AnyAsync(p => p.Kind == kind && p.Provenance == DataProvenance.Demo, ct))
            throw new EvidenceCopyUnavailableException(); // Only an explicit fresh session may follow teardown.
        db.EvidencePurposes.Add(new EvidencePurpose
        {
            Id = id,
            Provenance = DataProvenance.Demo,
            Kind = kind,
            SeasonId = seasonId,
            GrantId = access?.GrantId,
            GrantRevision = access?.Revision,
            HistoricalRequestId = historicalRequestId,
            CreatedAt = Now
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return id;
    }

    public async Task<Guid> StartFreshSyntheticPreviewAsync(CancellationToken ct = default)
    {
        if (db.Provenance != DataProvenance.Demo) throw new EvidenceCopyUnavailableException();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(ct);
        if (await db.EvidencePurposes.AnyAsync(p => p.Provenance == DataProvenance.Demo && p.Kind == EvidencePurposeKind.SyntheticPreview && p.OriginalEndedAt == null, ct)
            || await db.ExternalDataCaches.IgnoreQueryFilters().AnyAsync(c => c.Provenance == DataProvenance.Demo, ct)
            || await db.Subsessions.IgnoreQueryFilters().AnyAsync(c => c.Provenance == DataProvenance.Demo, ct)
            || await db.SubsessionResults.IgnoreQueryFilters().AnyAsync(c => c.Provenance == DataProvenance.Demo, ct)
            || await db.CarPercentileResults.IgnoreQueryFilters().AnyAsync(c => c.Provenance == DataProvenance.Demo, ct)
            || await db.Rivals.IgnoreQueryFilters().AnyAsync(c => c.Provenance == DataProvenance.Demo, ct)
            || await db.Set<ScopedDriverReference>().AnyAsync(c => c.Provenance == DataProvenance.Demo, ct)
            || await db.Set<PrivateDriverFollow>().AnyAsync(c => c.Provenance == DataProvenance.Demo, ct)
            || await db.SeasonCarBops.IgnoreQueryFilters().AnyAsync(c => c.Provenance == DataProvenance.Demo, ct)
            || await db.AuthorizedDriverNameCopies.IgnoreQueryFilters().AnyAsync(c => c.Provenance == DataProvenance.Demo, ct)
            || await db.Weeks.AnyAsync(w => w.DemoWeatherSummaryJson != null, ct)) throw new EvidenceCopyUnavailableException();
        var old = await db.EvidenceCopyMarkers.Where(c => c.Provenance == DataProvenance.Demo && c.VerifiedRemovedAt == null).ToListAsync(ct);
        foreach (var marker in old)
        {
            MarkUnavailable(marker, Now, Now);
            marker.VerifiedRemovedAt = Now;
        }
        var id = Guid.NewGuid();
        db.EvidencePurposes.Add(new()
        {
            Id = id,
            Provenance = DataProvenance.Demo,
            Kind = EvidencePurposeKind.SyntheticPreview,
            CreatedAt = Now
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return id;
    }

    public async Task<EvidenceWriteReceipt> CaptureAsync(Guid purposeId, EvidenceCopyKind kind, string key,
        IReadOnlyList<EvidenceSourceVersion>? sources = null, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(kind) || string.IsNullOrWhiteSpace(key)) throw new EvidenceCopyUnavailableException();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(ct);
        var purpose = await CurrentPurposeAsync(purposeId, kind, collection: true, ct);
        var sourceArray = sources?.ToArray() ?? [];
        await CheckSourcesAsync(sourceArray, purpose, ct);
        if (kind == EvidenceCopyKind.PersonalDerivative && sourceArray.Length == 0)
            throw new EvidenceCopyUnavailableException();
        var keyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        var version = await LastVersionAsync(purposeId, kind, keyHash, ct);
        await tx.CommitAsync(ct);
        return new(purpose.Id, purpose.Generation, purpose.EvidenceVersion, kind, keyHash, version, Now, sourceArray);
    }

    public async Task CompleteHistoricalRequestAsync(Guid purposeId, Guid requestId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(ct);
        var purpose = await db.EvidencePurposes.SingleAsync(p => p.Id == purposeId, ct);
        await db.Entry(purpose).ReloadAsync(ct);
        if (purpose.Provenance != db.Provenance || db.Provenance != DataProvenance.Demo || requestId == Guid.Empty
            || purpose.Kind != EvidencePurposeKind.AuthorizedHistory || purpose.HistoricalRequestId != requestId)
            throw new EvidenceCopyUnavailableException();
        if (purpose.HistoricalRequestEndedAt is null)
        {
            purpose.HistoricalRequestEndedAt = Now;
            purpose.EvidenceVersion++;
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    public async Task<EvidenceSourceVersion> CommitAsync(EvidenceWriteReceipt receipt, EvidenceBatch batch, CancellationToken ct = default)
    {
        // A failed attempt never leaves a fresh payload attached for a later unrelated SaveChanges.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await LockAsync(ct);
            if (receipt.KeyHash.Length != 64 || receipt.KeyHash.Any(c => !Uri.IsHexDigit(c)))
                throw new EvidenceCopyUnavailableException();
            var purpose = await CurrentPurposeAsync(receipt.PurposeId, receipt.Kind, collection: true, ct);
            if (purpose.Generation != receipt.Generation || purpose.EvidenceVersion != receipt.PurposeVersion || receipt.OriginalAcquiredAt > Now
                || receipt.OriginalAcquiredAt < purpose.CreatedAt
                || await LastVersionAsync(purpose.Id, receipt.Kind, receipt.KeyHash, ct) != receipt.ExpectedCopyVersion)
                throw new EvidenceCopyUnavailableException();
            await CheckSourcesAsync(receipt.Sources, purpose, ct);
            if (receipt.Kind == EvidenceCopyKind.PersonalDerivative && receipt.Sources.Count == 0)
                throw new EvidenceCopyUnavailableException();
            var marker = new EvidenceCopyMarker
            {
                Id = Guid.NewGuid(),
                PurposeId = purpose.Id,
                Generation = purpose.Generation,
                Provenance = purpose.Provenance,
                Kind = receipt.Kind,
                KeyHash = receipt.KeyHash,
                Version = receipt.ExpectedCopyVersion + 1,
                OriginalAcquiredAt = receipt.OriginalAcquiredAt
            };
            var prior = await db.EvidenceCopyMarkers.Where(c => c.PurposeId == purpose.Id && c.Kind == receipt.Kind
                && c.KeyHash == receipt.KeyHash && c.VerifiedRemovedAt == null).ToListAsync(ct);
            foreach (var old in prior)
            {
                MarkUnavailable(old, Now, Now);
                await InvalidateDependentsAsync(old.Id, Now, ct);
            }
            // Persist the original loss clock before physical DELETE triggers observe it.
            // Both writes remain inside this transaction and roll back together on failure.
            if (prior.Count != 0)
            {
                await db.SaveChangesAsync(ct);
                await RemovePhysicalAsync(prior.Select(c => c.Id).ToArray(), ct);
                foreach (var old in prior) old.VerifiedRemovedAt = Now;
            }
            await StageBatchAsync(marker, purpose, batch, ct);
            db.EvidenceCopyMarkers.Add(marker);
            foreach (var source in receipt.Sources)
                db.EvidenceCopyDependencies.Add(new() { CopyId = marker.Id, SourceCopyId = source.CopyId, SourceVersion = source.Version });
            await db.Entry(purpose).ReloadAsync(ct);
            purpose.EvidenceVersion++;
            // Marker and complete Field are one transaction. Insert the marker before payload triggers run.
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new(marker.Id, marker.Version);
        }
        catch
        {
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task EndPurposeAsync(Guid purposeId, DateTimeOffset originalEndAt, CancellationToken ct = default)
    {
        if (originalEndAt == default || originalEndAt > Now) throw new ArgumentException("An original purpose-end time is required.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(ct);
        var purpose = await db.EvidencePurposes.SingleAsync(p => p.Id == purposeId, ct);
        await db.Entry(purpose).ReloadAsync(ct);
        if (purpose.Provenance != db.Provenance || db.Provenance != DataProvenance.Demo) throw new EvidenceCopyUnavailableException();
        if (purpose.OriginalEndedAt is null) purpose.Generation++;
        purpose.OriginalEndedAt = Earliest(purpose.OriginalEndedAt, originalEndAt);
        var copies = await db.EvidenceCopyMarkers.Where(c => c.PurposeId == purposeId && c.VerifiedRemovedAt == null).ToListAsync(ct);
        foreach (var copy in copies)
        {
            var due = copy.Kind == EvidenceCopyKind.AuthorizedName
                ? EvidenceRetention.NameRemovalDueAt(purpose.OriginalEndedAt.Value)
                : EvidenceRetention.PurposeRemovalDueAt(purpose.OriginalEndedAt.Value);
            MarkUnavailable(copy, purpose.OriginalEndedAt.Value, due);
            await InvalidateDependentsAsync(copy.Id, purpose.OriginalEndedAt.Value, ct);
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<EvidenceCleanupOutcome> ReconcileAsync(CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await LockAsync(ct);
            // Database fences can invalidate sources/descendants through another context or raw
            // SQL. Refresh tracked metadata in one query before making any erasure decision.
            var tracked = db.ChangeTracker.Entries<EvidenceCopyMarker>().ToDictionary(e => e.Entity.Id);
            var copies = await db.EvidenceCopyMarkers.AsNoTracking().Where(c => c.VerifiedRemovedAt == null).ToListAsync(ct);
            for (var index = 0; index < copies.Count; index++)
            {
                var copy = copies[index];
                if (tracked.TryGetValue(copy.Id, out var entry))
                {
                    entry.CurrentValues.SetValues(copy);
                    entry.OriginalValues.SetValues(copy);
                    entry.State = EntityState.Unchanged;
                    copies[index] = entry.Entity;
                }
                else db.EvidenceCopyMarkers.Attach(copy);
            }
            foreach (var copy in copies.Where(c => c.ExpiresAt <= Now))
            {
                MarkUnavailable(copy, copy.ExpiresAt!.Value, EvidenceRetention.MappedRemovalDueAt(copy.ExpiresAt.Value));
                await InvalidateDependentsAsync(copy.Id, copy.ExpiresAt.Value, ct);
            }
            // Invalidation may discover descendants not in the initial tracked state; reload the pending set.
            var pending = copies
                .Where(c => c.VerifiedRemovedAt is null && c.UnavailableAt is not null
                    && !(c.Kind == EvidenceCopyKind.PrivateUpload && c.RemovalDueAt >= c.UnavailableAt.Value.AddDays(90)
                        && Now < c.UnavailableAt.Value.AddDays(90))).ToArray();
            if (pending.Length != 0)
            {
                // Remove promptly, rather than treating a maximum deadline as a mandatory grace period.
                await db.SaveChangesAsync(ct);
                await RemovePhysicalAsync(pending.Select(c => c.Id).ToArray(), ct);
                foreach (var copy in pending) copy.VerifiedRemovedAt = Now;
            }
            // User deletion also removes ordinary account/profile/credential stores and legacy
            // uploads. Minimal proof/grant/journal metadata has no cascading User relationship.
            var deletedUsers = await db.DriverAuthorizationGrants.Where(g => db.DriverLifecycleOperations
                .Any(o => o.GrantId == g.Id && o.Kind == DriverLifecycleKind.DeleteUser && o.CompletedAt != null))
                .Select(g => g.UserId).Distinct().ToArrayAsync(ct);
            if (deletedUsers.Length != 0)
            {
                var deletedGrantIds = await db.DriverAuthorizationGrants.Where(g => deletedUsers.Contains(g.UserId)).Select(g => g.Id).ToArrayAsync(ct);
                await db.Set<ScopedDriverReference>().Where(r => deletedGrantIds.Contains(r.RecipientGrantId) || deletedGrantIds.Contains(r.TargetGrantId)).ExecuteDeleteAsync(ct);
                await db.Set<PrivateDriverFollow>().Where(f => deletedGrantIds.Contains(f.RecipientGrantId) || deletedGrantIds.Contains(f.TargetGrantId)).ExecuteDeleteAsync(ct);
                await db.UploadedLaps.IgnoreQueryFilters().Where(l => deletedUsers.Contains(l.UserId)).ExecuteDeleteAsync(ct);
                await db.DriverProofReceipts.Where(p => deletedUsers.Contains(p.UserId))
                    .ExecuteUpdateAsync(u => u.SetProperty(p => p.Authority, string.Empty), ct);
                await db.Users.Where(u => deletedUsers.Contains(u.Id)).ExecuteDeleteAsync(ct);
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new(pending.Length, 0, 0); // Backup observation deliberately has its own, unverified outcome.
        }
        catch
        {
            db.ChangeTracker.Clear(); // Rolled-back erasure must not leave an attached verified-removal claim.
            throw;
        }
    }

    public async Task<EvidenceCleanupOutcome> InspectAsync(CancellationToken ct = default)
    {
        var pending = db.EvidenceCopyMarkers.Where(c => c.VerifiedRemovedAt == null
            && (c.UnavailableAt != null || c.ExpiresAt <= Now));
        return new(await db.EvidenceCopyMarkers.CountAsync(c => c.VerifiedRemovedAt != null, ct),
            await pending.CountAsync(ct), await pending.CountAsync(c => c.RemovalDueAt < Now, ct));
    }

    private async Task<EvidencePurpose> CurrentPurposeAsync(Guid id, EvidenceCopyKind kind, bool collection, CancellationToken ct)
    {
        var purpose = await db.EvidencePurposes.SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw new EvidenceCopyUnavailableException();
        await db.Entry(purpose).ReloadAsync(ct);
        if (purpose.Provenance != db.Provenance || purpose.Provenance != DataProvenance.Demo || purpose.OriginalEndedAt is not null
            || !Enum.IsDefined(purpose.Kind)) throw new EvidenceCopyUnavailableException();
        if (purpose.Kind == EvidencePurposeKind.IndependentOfficial
            && (kind is EvidenceCopyKind.PersonalDerivative or EvidenceCopyKind.Follow or EvidenceCopyKind.AuthorizedName or EvidenceCopyKind.PrivateUpload
                || collection && !await db.Seasons.AnyAsync(s => s.Id == purpose.SeasonId && s.Active, ct)))
            throw new EvidenceCopyUnavailableException();
        if (purpose.Kind == EvidencePurposeKind.AuthorizedHistory && kind is not (EvidenceCopyKind.OfficialField or EvidenceCopyKind.Bop or EvidenceCopyKind.Weather))
            throw new EvidenceCopyUnavailableException();
        if (collection && purpose.Kind == EvidencePurposeKind.AuthorizedHistory
            && (purpose.HistoricalRequestId is null || purpose.HistoricalRequestEndedAt is not null)) throw new EvidenceCopyUnavailableException();
        if (purpose.Kind is EvidencePurposeKind.Personal or EvidencePurposeKind.Sharing || collection && purpose.Kind == EvidencePurposeKind.AuthorizedHistory)
        {
            var grant = await db.DriverAuthorizationGrants.AsNoTracking().SingleOrDefaultAsync(g => g.Id == purpose.GrantId, ct)
                ?? throw new EvidenceCopyUnavailableException();
            await CheckAccessAsync(new(new(grant.UserId, grant.CustomerId, grant.Provenance), grant.Id,
                purpose.GrantRevision ?? 0, purpose.Kind == EvidencePurposeKind.Sharing ? DriverConsentScope.Sharing : DriverConsentScope.Personal,
                grant.AuthorizedDriverName), ct);
        }
        if (kind == EvidenceCopyKind.AuthorizedName && purpose.Kind is not (EvidencePurposeKind.Personal or EvidencePurposeKind.Sharing))
            throw new EvidenceCopyUnavailableException();
        if (kind == EvidenceCopyKind.PrivateUpload && purpose.Kind != EvidencePurposeKind.Personal)
            throw new EvidenceCopyUnavailableException();
        if (purpose.Kind == EvidencePurposeKind.Personal && kind is not (EvidenceCopyKind.MappedCache
                or EvidenceCopyKind.PersonalDerivative or EvidenceCopyKind.Follow or EvidenceCopyKind.AuthorizedName or EvidenceCopyKind.PrivateUpload)
            || purpose.Kind == EvidencePurposeKind.Sharing && kind != EvidenceCopyKind.AuthorizedName)
            throw new EvidenceCopyUnavailableException();
        return purpose;
    }

    private async Task CheckAccessAsync(DriverAccess access, CancellationToken ct)
    {
        if (journal is null || !(await journal.ReadCurrentAsync(access.Scope, ct)).Allows(access.Revision, access.Purpose)
            || await new DriverAuthorityStore(db, clock).ResolveAsync(access.Scope, access.Purpose, ct) is not { } current
            || current.GrantId != access.GrantId || current.Revision != access.Revision)
            throw new EvidenceCopyUnavailableException();
    }

    internal async Task CheckSourcesAsync(IReadOnlyList<EvidenceSourceVersion> sources, EvidencePurpose target, CancellationToken ct)
    {
        if (sources.Select(s => s.CopyId).Distinct().Count() != sources.Count) throw new EvidenceCopyUnavailableException();
        var ids = sources.Select(s => s.CopyId).ToArray();
        var markers = await db.EvidenceCopyMarkers.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var retainedPurposes = new Dictionary<Guid, EvidencePurpose>();
        foreach (var source in sources)
        {
            if (!markers.TryGetValue(source.CopyId, out var marker) || marker.Version != source.Version
                || marker.Provenance != target.Provenance || marker.UnavailableAt != null || marker.VerifiedRemovedAt != null
                || marker.ExpiresAt <= Now)
                throw new EvidenceCopyUnavailableException();
            if (!retainedPurposes.TryGetValue(marker.PurposeId, out var retained))
            {
                retained = await CurrentPurposeAsync(marker.PurposeId, marker.Kind, collection: false, ct);
                retainedPurposes[marker.PurposeId] = retained;
            }
            if (retained.Generation != marker.Generation
                || retained.Id != target.Id && retained.Kind is not (EvidencePurposeKind.IndependentOfficial or EvidencePurposeKind.AuthorizedHistory)
                    && !(retained.Kind == EvidencePurposeKind.Personal && target.Kind == EvidencePurposeKind.Personal
                        && retained.GrantId == target.GrantId && retained.GrantRevision == target.GrantRevision))
                throw new EvidenceCopyUnavailableException(); // A new purpose cannot launder a private source into shared evidence.
        }
    }

    private async Task StageBatchAsync(EvidenceCopyMarker marker, EvidencePurpose purpose, EvidenceBatch batch, CancellationToken ct)
    {
        void Stamp(IManagedEvidence copy)
        {
            if (copy.Provenance != DataProvenance.Unknown && copy.Provenance != purpose.Provenance)
                throw new EvidenceCopyUnavailableException();
            copy.Provenance = purpose.Provenance;
            copy.EvidenceCopyId = marker.Id;
            db.Add(copy);
        }
        switch (batch)
        {
            case ScheduleEvidenceBatch schedule when marker.Kind == EvidenceCopyKind.Weather:
                var weekIds = schedule.Weather.Select(w => w.WeekId).ToArray();
                if (weekIds.Distinct().Count() != weekIds.Length) throw new EvidenceCopyUnavailableException();
                var scheduleWeeks = await db.Weeks.Where(w => weekIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, ct);
                foreach (var item in schedule.Weather)
                {
                    if (!scheduleWeeks.TryGetValue(item.WeekId, out var scheduleWeek) || scheduleWeek.SeasonId != purpose.SeasonId)
                        throw new EvidenceCopyUnavailableException();
                    scheduleWeek.DemoWeatherSummaryJson = item.Payload;
                    scheduleWeek.DemoWeatherEvidenceCopyId = marker.Id;
                }
                var mappedBops = schedule.Bops.Select(item => (Copy: item,
                    Hash: Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"bop:{item.SeasonId}:{item.RaceWeekIndex}:{item.CarId}"))))).ToArray();
                if (mappedBops.Select(item => item.Hash).Distinct().Count() != mappedBops.Length
                    || mappedBops.Any(item => item.Copy.SeasonId != purpose.SeasonId
                        || item.Copy.Provenance != DataProvenance.Unknown && item.Copy.Provenance != purpose.Provenance))
                    throw new EvidenceCopyUnavailableException();
                var bopHashes = mappedBops.Select(item => item.Hash).ToArray();
                var versions = await db.EvidenceCopyMarkers.Where(c => c.PurposeId == purpose.Id && c.Kind == EvidenceCopyKind.Bop
                    && bopHashes.Contains(c.KeyHash)).GroupBy(c => c.KeyHash).Select(g => new { Hash = g.Key, Version = g.Max(c => c.Version) })
                    .ToDictionaryAsync(c => c.Hash, c => c.Version, ct);
                var carIds = mappedBops.Select(item => item.Copy.CarId).ToArray();
                var raceWeeks = mappedBops.Select(item => item.Copy.RaceWeekIndex).ToArray();
                var oldBops = await db.SeasonCarBops.IgnoreQueryFilters().Where(b => b.Provenance == purpose.Provenance
                    && b.SeasonId == purpose.SeasonId && carIds.Contains(b.CarId) && raceWeeks.Contains(b.RaceWeekIndex)).ToListAsync(ct);
                var keys = mappedBops.Select(item => (item.Copy.RaceWeekIndex, item.Copy.CarId)).ToHashSet();
                var replaced = oldBops.Where(b => keys.Contains((b.RaceWeekIndex, b.CarId))).ToArray();
                if (replaced.Any(b => b.EvidenceCopyId is null)) throw new EvidenceCopyUnavailableException();
                var replacedIds = replaced.Select(b => b.EvidenceCopyId!.Value).ToArray();
                await db.SeasonCarBops.IgnoreQueryFilters().Where(b => b.Provenance == purpose.Provenance
                    && replacedIds.Contains(b.EvidenceCopyId!.Value)).ExecuteDeleteAsync(ct);
                foreach (var entry in replaced) db.Entry(entry).State = EntityState.Detached;
                foreach (var (item, hash) in mappedBops)
                {
                    var bopMarker = new EvidenceCopyMarker
                    {
                        Id = Guid.NewGuid(),
                        PurposeId = purpose.Id,
                        Generation = purpose.Generation,
                        Kind = EvidenceCopyKind.Bop,
                        KeyHash = hash,
                        Version = versions.GetValueOrDefault(hash) + 1,
                        Provenance = purpose.Provenance,
                        OriginalAcquiredAt = marker.OriginalAcquiredAt
                    };
                    db.EvidenceCopyMarkers.Add(bopMarker);
                    item.Provenance = purpose.Provenance;
                    item.EvidenceCopyId = bopMarker.Id;
                    db.SeasonCarBops.Add(item);
                }
                break;
            case MappedCacheBatch cache when marker.Kind == EvidenceCopyKind.MappedCache:
                if (cache.Copy.FetchedAt != marker.OriginalAcquiredAt || cache.Copy.ExpiresAt <= Now)
                    throw new EvidenceCopyUnavailableException();
                marker.ExpiresAt = cache.Copy.ExpiresAt;
                marker.RemovalDueAt = EvidenceRetention.MappedRemovalDueAt(cache.Copy.ExpiresAt);
                Stamp(cache.Copy);
                break;
            case OfficialFieldBatch field when marker.Kind == EvidenceCopyKind.OfficialField:
                if (!field.Race.OfficialSession || purpose.SeasonId is { } season && field.Race.SeasonId != season
                    || field.Results.Any(r => r.SubsessionId != field.Race.Id || r.CustId <= 0)
                    || field.Results.Select(r => r.CustId).Distinct().Count() != field.Results.Count)
                    throw new EvidenceCopyUnavailableException();
                Stamp(field.Race);
                foreach (var result in field.Results)
                {
                    if (purpose.Kind != EvidencePurposeKind.SyntheticPreview) result.DisplayName = null;
                    Stamp(result);
                }
                break;
            case BopBatch bop when marker.Kind == EvidenceCopyKind.Bop:
                if (purpose.SeasonId is { } bopSeason && bop.Copy.SeasonId != bopSeason) throw new EvidenceCopyUnavailableException();
                Stamp(bop.Copy);
                break;
            case WeatherBatch weather when marker.Kind == EvidenceCopyKind.Weather:
                var week = await db.Weeks.SingleAsync(w => w.Id == weather.WeekId, ct);
                if (purpose.SeasonId is { } weatherSeason && week.SeasonId != weatherSeason) throw new EvidenceCopyUnavailableException();
                week.DemoWeatherSummaryJson = weather.Payload;
                week.DemoWeatherEvidenceCopyId = marker.Id;
                break;
            case PercentileBatch derivative when marker.Kind == EvidenceCopyKind.PersonalDerivative:
                if (purpose.Kind != EvidencePurposeKind.SyntheticPreview)
                {
                    var owner = await db.DriverAuthorizationGrants.AsNoTracking().SingleAsync(g => g.Id == purpose.GrantId, ct);
                    if (derivative.Copy.UserId != owner.UserId) throw new EvidenceCopyUnavailableException();
                }
                Stamp(derivative.Copy);
                break;
            case FollowBatch follow when marker.Kind == EvidenceCopyKind.Follow && purpose.Kind is EvidencePurposeKind.SyntheticPreview or EvidencePurposeKind.Personal:
                if (purpose.Kind == EvidencePurposeKind.Personal)
                {
                    var owner = await db.DriverAuthorizationGrants.AsNoTracking().SingleAsync(g => g.Id == purpose.GrantId, ct);
                    if (follow.Copy.UserId != owner.UserId) throw new EvidenceCopyUnavailableException();
                    follow.Copy.DisplayName = string.Empty;
                }
                Stamp(follow.Copy);
                break;
            case AuthorizedNameBatch name when marker.Kind == EvidenceCopyKind.AuthorizedName && name.GrantId == purpose.GrantId:
                var grant = await db.DriverAuthorizationGrants.AsNoTracking().SingleAsync(g => g.Id == name.GrantId, ct);
                if (string.IsNullOrEmpty(grant.AuthorizedDriverName)) throw new EvidenceCopyUnavailableException();
                Stamp(new AuthorizedDriverNameCopy { Id = Guid.NewGuid(), GrantId = grant.Id, DriverName = grant.AuthorizedDriverName });
                break;
            default: throw new EvidenceCopyUnavailableException();
        }
    }

    internal async Task InvalidateDependentsAsync(Guid sourceId, DateTimeOffset lossAt, CancellationToken ct)
    {
        var frontier = new[] { sourceId };
        var visited = new HashSet<Guid> { sourceId };
        while (frontier.Length != 0)
        {
            var children = await db.EvidenceCopyDependencies.Where(d => frontier.Contains(d.SourceCopyId)).Select(d => d.CopyId).Distinct().ToListAsync(ct);
            frontier = children.Where(visited.Add).ToArray();
            foreach (var copy in await db.EvidenceCopyMarkers.Where(c => frontier.Contains(c.Id)).ToListAsync(ct))
            {
                MarkUnavailable(copy, lossAt, copy.Kind == EvidenceCopyKind.AuthorizedName
                    ? EvidenceRetention.NameRemovalDueAt(lossAt) : EvidenceRetention.PurposeRemovalDueAt(lossAt));
            }
        }
    }

    internal async Task ApplyGrantLossAsync(DriverAuthorizationGrant grant, DriverLifecycleIntent intent, CancellationToken ct)
    {
        var purposes = await db.EvidencePurposes.Where(p => p.GrantId == grant.Id
            && (p.OriginalEndedAt == null || intent.Kind == DriverLifecycleKind.DeleteUser)).ToListAsync(ct);
        foreach (var purpose in purposes)
        {
            if (purpose.Kind == EvidencePurposeKind.AuthorizedHistory) continue; // Collection closes through the original grant revision; independent retention is separate.
            if (intent.Kind == DriverLifecycleKind.WithdrawSharing && purpose.Kind == EvidencePurposeKind.Personal)
            {
                purpose.GrantRevision = grant.Revision;
                purpose.EvidenceVersion++; // Retain Personal copies, but reject preparation under the old revision.
                continue;
            }
            if (purpose.OriginalEndedAt is null) purpose.Generation++;
            purpose.OriginalEndedAt = Earliest(purpose.OriginalEndedAt, intent.OriginalLossAt);
            var copies = await db.EvidenceCopyMarkers.Where(c => c.PurposeId == purpose.Id && c.VerifiedRemovedAt == null).ToListAsync(ct);
            foreach (var copy in copies)
            {
                var scope = purpose.Kind == EvidencePurposeKind.Sharing ? DriverConsentScope.Sharing : DriverConsentScope.Personal;
                var due = copy.Kind == EvidenceCopyKind.AuthorizedName ? EvidenceRetention.NameRemovalDueAt(intent.OriginalLossAt)
                    : DriverAuthorizationPolicy.CleanupDueAt(intent.Kind, scope, intent.OriginalLossAt);
                MarkUnavailable(copy, intent.OriginalLossAt, due);
                await InvalidateDependentsAsync(copy.Id, intent.OriginalLossAt, ct);
            }
        }
    }

    internal async Task RefreshGrantGenerationAsync(DriverAuthorizationGrant grant, CancellationToken ct)
    {
        var purposes = await db.EvidencePurposes.Where(p => p.GrantId == grant.Id && p.OriginalEndedAt == null
            && (p.Kind == EvidencePurposeKind.Personal || p.Kind == EvidencePurposeKind.Sharing)).ToListAsync(ct);
        foreach (var purpose in purposes)
        {
            purpose.GrantRevision = grant.Revision;
            purpose.EvidenceVersion++;
            foreach (var copy in await db.EvidenceCopyMarkers.Where(c => c.PurposeId == purpose.Id
                         && c.Kind == EvidenceCopyKind.AuthorizedName && c.VerifiedRemovedAt == null).ToListAsync(ct))
            {
                MarkUnavailable(copy, Now, EvidenceRetention.NameRemovalDueAt(Now));
                await InvalidateDependentsAsync(copy.Id, Now, ct);
            }
        }
    }

    private async Task RemovePhysicalAsync(Guid[] ids, CancellationToken ct)
    {
        await db.ExternalDataCaches.IgnoreQueryFilters().Where(c => c.EvidenceCopyId != null && ids.Contains(c.EvidenceCopyId.Value)).ExecuteDeleteAsync(ct);
        await db.SubsessionResults.IgnoreQueryFilters().Where(c => c.EvidenceCopyId != null && ids.Contains(c.EvidenceCopyId.Value)).ExecuteDeleteAsync(ct);
        await db.Subsessions.IgnoreQueryFilters().Where(c => c.EvidenceCopyId != null && ids.Contains(c.EvidenceCopyId.Value)).ExecuteDeleteAsync(ct);
        await db.SeasonCarBops.IgnoreQueryFilters().Where(c => c.EvidenceCopyId != null && ids.Contains(c.EvidenceCopyId.Value)).ExecuteDeleteAsync(ct);
        await db.CarPercentileResults.IgnoreQueryFilters().Where(c => c.EvidenceCopyId != null && ids.Contains(c.EvidenceCopyId.Value)).ExecuteDeleteAsync(ct);
        await db.PrivateUploadSessions.Where(c => ids.Contains(c.EvidenceCopyId)).ExecuteDeleteAsync(ct);
        await db.Rivals.IgnoreQueryFilters().Where(c => c.EvidenceCopyId != null && ids.Contains(c.EvidenceCopyId.Value)).ExecuteDeleteAsync(ct);
        await db.AuthorizedDriverNameCopies.IgnoreQueryFilters().Where(c => c.EvidenceCopyId != null && ids.Contains(c.EvidenceCopyId.Value)).ExecuteDeleteAsync(ct);
        await db.Weeks.Where(w => w.DemoWeatherEvidenceCopyId != null && ids.Contains(w.DemoWeatherEvidenceCopyId.Value)).ExecuteUpdateAsync(u => u
            .SetProperty(w => w.DemoWeatherSummaryJson, (string?)null).SetProperty(w => w.DemoWeatherEvidenceCopyId, (Guid?)null), ct);
        await db.Weeks.Where(w => w.WeatherEvidenceCopyId != null && ids.Contains(w.WeatherEvidenceCopyId.Value)).ExecuteUpdateAsync(u => u
            .SetProperty(w => w.WeatherSummaryJson, (string?)null).SetProperty(w => w.WeatherEvidenceCopyId, (Guid?)null), ct);
    }

    private async Task<long> LastVersionAsync(Guid purpose, EvidenceCopyKind kind, string key, CancellationToken ct) =>
        await db.EvidenceCopyMarkers.Where(c => c.PurposeId == purpose && c.Kind == kind && c.KeyHash == key)
            .Select(c => (long?)c.Version).MaxAsync(ct) ?? 0;
    private DateTimeOffset Now => DriverAuthorizationPolicy.DurableTime(clock.GetUtcNow());
    private static DateTimeOffset Earliest(DateTimeOffset? previous, DateTimeOffset incoming) => previous < incoming ? previous.Value : incoming;
    internal static void MarkUnavailable(EvidenceCopyMarker copy, DateTimeOffset lossAt, DateTimeOffset dueAt)
    {
        copy.UnavailableAt = Earliest(copy.UnavailableAt, lossAt);
        copy.RemovalDueAt = Earliest(copy.RemovalDueAt, dueAt);
    }
    public async Task WithdrawNameBearingCacheAsync(Guid copyId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(ct);
        var copy = await db.EvidenceCopyMarkers.SingleAsync(c => c.Id == copyId, ct);
        await db.Entry(copy).ReloadAsync(ct);
        MarkUnavailable(copy, Now, EvidenceRetention.NameRemovalDueAt(Now));
        var purpose = await db.EvidencePurposes.SingleAsync(p => p.Id == copy.PurposeId, ct);
        await db.Entry(purpose).ReloadAsync(ct);
        purpose.EvidenceVersion++;
        await InvalidateDependentsAsync(copy.Id, copy.UnavailableAt!.Value, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
    private async Task LockAsync(CancellationToken ct)
    {
        if (db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite") return; // SQLite transactions serialize synthetic unit fixtures.
        if (!db.Database.IsNpgsql()) throw new EvidenceCopyUnavailableException();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({370L})", ct);
    }
}
