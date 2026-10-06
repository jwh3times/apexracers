using System.Security.Cryptography;
using System.Text;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Data;

/// <summary>Owns typed upload persistence and recovery within Driver/copy coordination.
/// Caller-provided identifiers, claims, file names and recorder names never establish authority.</summary>
public sealed class PrivateUploadStore(AppDbContext db, TimeProvider clock, IDriverEnforcementJournal journal)
{
    public async Task<PrivateUploadReceipt> CaptureAsync(DriverAccess owner, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(ct);
        await CheckOwnerAsync(owner, ct);
        var purpose = await db.EvidencePurposes.FirstOrDefaultAsync(p => p.GrantId == owner.GrantId
            && p.Kind == EvidencePurposeKind.Personal && p.OriginalEndedAt == null && p.GrantRevision == owner.Revision, ct);
        if (purpose is null)
        {
            purpose = new EvidencePurpose
            {
                Id = Guid.NewGuid(),
                Provenance = DataProvenance.Demo,
                Kind = EvidencePurposeKind.Personal,
                GrantId = owner.GrantId,
                GrantRevision = owner.Revision,
                CreatedAt = Now
            };
            db.EvidencePurposes.Add(purpose);
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return new(owner, purpose.Id, purpose.Generation, purpose.EvidenceVersion, Now);
    }

    public async Task<Guid> CommitAsync(PrivateUploadReceipt receipt, PrivateUploadData data, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await LockAsync(ct);
            await CheckOwnerAsync(receipt.Owner, ct);
            var purpose = await db.EvidencePurposes.AsNoTracking().SingleOrDefaultAsync(p => p.Id == receipt.PurposeId, ct);
            if (purpose is null || purpose.Kind != EvidencePurposeKind.Personal || purpose.Provenance != DataProvenance.Demo
                || purpose.GrantId != receipt.Owner.GrantId || purpose.GrantRevision != receipt.Owner.Revision
                || purpose.Generation != receipt.Generation || purpose.EvidenceVersion != receipt.PurposeVersion
                || purpose.OriginalEndedAt is not null || receipt.OriginalAcquiredAt < purpose.CreatedAt || receipt.OriginalAcquiredAt > Now)
                throw new EvidenceCopyUnavailableException();
            if (!Enum.IsDefined(data.SessionType) || data.Laps.Count == 0
                || data.Laps.Any(l => l.LapNumber <= 0 || !double.IsFinite(l.LapTimeSeconds) || l.LapTimeSeconds <= 0)
                || data.Laps.Select(l => l.LapNumber).Distinct().Count() != data.Laps.Count)
                throw new ArgumentException("A valid timed upload is required.");
            if (!await db.Cars.AnyAsync(c => c.Id == data.CarId, ct) || !await db.Tracks.AnyAsync(t => t.Id == data.TrackId, ct))
                throw new InvalidOperationException("This telemetry's car or track is not in the catalog yet.");
            var scope = receipt.Owner.Scope;
            var existing = await db.PrivateUploadSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Provenance == scope.Provenance
                && s.UserId == scope.UserId && s.CustomerId == scope.CustomerId && s.CarId == data.CarId
                && s.TrackId == data.TrackId && s.RecordedAt == data.RecordedAt, ct);
            if (existing is not null)
            {
                if (!await AvailableSessions(receipt.Owner).AnyAsync(s => s.Id == existing.Id, ct)) throw new EvidenceCopyUnavailableException();
                return existing.EvidenceCopyId;
            }
            var marker = new EvidenceCopyMarker
            {
                Id = Guid.NewGuid(),
                PurposeId = purpose.Id,
                Generation = purpose.Generation,
                Kind = EvidenceCopyKind.PrivateUpload,
                Provenance = scope.Provenance,
                Version = 1,
                KeyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                    $"private-upload:{scope.UserId}:{scope.CustomerId}:{data.CarId}:{data.TrackId}:{data.RecordedAt.UtcTicks}"))),
                OriginalAcquiredAt = receipt.OriginalAcquiredAt
            };
            db.EvidenceCopyMarkers.Add(marker);
            db.PrivateUploadSessions.Add(new PrivateUploadSession
            {
                Id = Guid.NewGuid(),
                UserId = scope.UserId,
                CustomerId = scope.CustomerId,
                Provenance = scope.Provenance,
                EvidenceCopyId = marker.Id,
                CarId = data.CarId,
                TrackId = data.TrackId,
                RecordedAt = data.RecordedAt,
                SessionType = data.SessionType,
                Laps = data.Laps.Select(l => new PrivateUploadedLap
                {
                    Id = Guid.NewGuid(),
                    LapNumber = l.LapNumber,
                    LapTimeSeconds = l.LapTimeSeconds
                }).ToList()
            });
            // A newly contributed lap can change every owner summary for this car/track.
            // Invalidate derivatives while keeping the earlier private sources available,
            // and advance preparation version so delayed writers cannot publish old input sets.
            var priorSources = await AvailableSessions(receipt.Owner).Where(s => s.CarId == data.CarId && s.TrackId == data.TrackId)
                .Select(s => s.EvidenceCopyId).ToArrayAsync(ct);
            var copies = new EvidenceCopyLifecycle(db, clock, journal);
            foreach (var source in priorSources) await copies.InvalidateDependentsAsync(source, Now, ct);
            var currentPurpose = await db.EvidencePurposes.SingleAsync(p => p.Id == receipt.PurposeId, ct);
            await db.Entry(currentPurpose).ReloadAsync(ct);
            currentPurpose.EvidenceVersion++;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return marker.Id;
        }
        catch { db.ChangeTracker.Clear(); throw; }
    }

    public async Task<IReadOnlyList<PrivateUploadedBest>> ReadBestsAsync(DriverAccess owner, CancellationToken ct = default) =>
        (await ReadSnapshotAsync(owner, ct)).Bests;

    public async Task<PrivateUploadSnapshot> ReadSnapshotAsync(DriverAccess owner, CancellationToken ct = default)
    {
        await CheckOwnerAsync(owner, ct);
        var sessions = await AvailableSessions(owner).Include(s => s.Laps).ToListAsync(ct);
        var cars = await db.Cars.Where(c => sessions.Select(s => s.CarId).Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var tracks = await db.Tracks.Where(t => sessions.Select(s => s.TrackId).Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        var ids = sessions.Select(s => s.EvidenceCopyId).ToArray();
        var sources = await db.EvidenceCopyMarkers.AsNoTracking().Where(c => ids.Contains(c.Id))
            .Select(c => new EvidenceSourceVersion(c.Id, c.Version)).ToListAsync(ct);
        return new(sessions.GroupBy(s => new { s.CarId, s.TrackId }).Select(g => new PrivateUploadedBest(g.Key.CarId,
            g.Key.TrackId, cars[g.Key.CarId].Name, tracks[g.Key.TrackId].Name,
            ConfigurationName.NullIfAbsent(tracks[g.Key.TrackId].ConfigName), g.SelectMany(s => s.Laps).Min(l => l.LapTimeSeconds),
            g.Sum(s => s.Laps.Count), g.Max(s => s.RecordedAt))).OrderByDescending(b => b.LastRecordedAt).ToList(), sources);
    }

    public async Task<bool> SnapshotCurrentAsync(DriverAccess owner, IReadOnlyList<EvidenceSourceVersion> sources,
        CancellationToken ct = default)
    {
        try { await CheckOwnerAsync(owner, ct); }
        catch (EvidenceCopyUnavailableException) { return false; }
        var ids = sources.Select(s => s.CopyId).ToArray();
        var current = await (from session in AvailableSessions(owner)
                             join marker in db.EvidenceCopyMarkers.AsNoTracking() on session.EvidenceCopyId equals marker.Id
                             where ids.Contains(marker.Id)
                             select new EvidenceSourceVersion(marker.Id, marker.Version)).ToListAsync(ct);
        return sources.Count == current.Count && sources.All(s => current.Contains(s));
    }

    internal IQueryable<PrivateUploadSession> AvailableSessions(DriverAccess owner) => db.PrivateUploadSessions.AsNoTracking()
        .Where(s => s.UserId == owner.Scope.UserId && s.CustomerId == owner.Scope.CustomerId && s.Provenance == owner.Scope.Provenance
            && db.EvidenceCopyMarkers.Any(c => c.Id == s.EvidenceCopyId && c.Kind == EvidenceCopyKind.PrivateUpload
                && c.UnavailableAt == null && c.VerifiedRemovedAt == null && db.EvidencePurposes.Any(p => p.Id == c.PurposeId
                    && p.Kind == EvidencePurposeKind.Personal && p.OriginalEndedAt == null && p.Generation == c.Generation
                    && p.GrantId == owner.GrantId && p.GrantRevision == owner.Revision)));

    public async Task<PrivateOwnerPercentile?> CalculatePercentileAsync(DriverAccess owner, Guid weekId, int carId,
        CancellationToken ct = default)
    {
        await CheckOwnerAsync(owner, ct);
        // Capture before reading sources. A concurrent loss, source replacement or correction
        // invalidates the commit; freshly calculated output is never returned after a failed commit.
        var uploadReceipt = await CaptureAsync(owner, ct);
        var week = await db.Weeks.AsNoTracking().SingleOrDefaultAsync(w => w.Id == weekId, ct);
        if (week is null) return null;
        var weeks = await db.Weeks.Where(w => w.SeasonId == week.SeasonId)
            .Select(w => new { w.RaceWeekIndex, w.StartDate, w.EndTime }).ToListAsync(ct);
        var window = RaceWeekWindow.ForSeason(weeks.Select(w => (w.RaceWeekIndex, w.StartDate, w.EndTime)))
            .Single(w => w.RaceWeekIndex == week.RaceWeekIndex).Window;
        var sessions = await AvailableSessions(owner).Include(s => s.Laps).Where(s => s.CarId == carId
            && s.TrackId == week.TrackId && s.RecordedAt >= window.Start && s.RecordedAt < window.End).ToListAsync(ct);
        var results = await db.SubsessionResults.Where(r => r.CarId == carId && r.Subsession.SeasonId == week.SeasonId
            && r.Subsession.RaceWeekIndex == week.RaceWeekIndex && r.Subsession.OfficialSession
            && r.BestLapSeconds > 0).ToListAsync(ct);
        var byDriver = results.GroupBy(r => r.CustId).ToDictionary(g => g.Key, g => g.Min(r => r.BestLapSeconds));
        var uploaded = sessions.SelectMany(s => s.Laps).Select(l => (double?)l.LapTimeSeconds).DefaultIfEmpty().Min();
        var best = PersonalBest.Select(byDriver.TryGetValue(owner.Scope.CustomerId, out var raced) ? raced : (double?)null, uploaded);
        if (best is null || results.Count == 0) return null;
        var other = byDriver.Where(d => d.Key != owner.Scope.CustomerId).Select(d => d.Value).ToList();
        var percentile = new PrivateOwnerPercentile(best.Value.LapSeconds, best.Value.Evidence,
            FieldPercentile.Rank(best.Value.LapSeconds, other), FieldPercentile.Position(best.Value.LapSeconds, other),
            FieldPercentile.TopSharePercent(best.Value.LapSeconds, other), FieldPercentile.FieldSize(other));
        var sourceIds = sessions.Select(s => s.EvidenceCopyId).Concat(results.Select(r => r.EvidenceCopyId!.Value)).Distinct().ToArray();
        var sources = await db.EvidenceCopyMarkers.AsNoTracking().Where(c => sourceIds.Contains(c.Id))
            .Select(c => new EvidenceSourceVersion(c.Id, c.Version)).ToListAsync(ct);
        var copies = new EvidenceCopyLifecycle(db, clock, journal);
        var seriesId = await db.Seasons.Where(s => s.Id == week.SeasonId).Select(s => s.SeriesId).SingleAsync(ct);
        // The copy module atomically replaces this purpose/key. Reconcile withdrawn older
        // purposes before rebuilding; original source clocks and dormant retention remain intact.
        await copies.ReconcileAsync(ct);
        var receipt = await copies.CaptureAsync(uploadReceipt.PurposeId, EvidenceCopyKind.PersonalDerivative,
            $"private-percentile:{owner.Scope.UserId}:{weekId}:{carId}", sources, ct);
        await copies.CommitAsync(receipt, new PercentileBatch(new CarPercentileResult
        {
            Id = Guid.NewGuid(),
            UserId = owner.Scope.UserId,
            CarId = carId,
            WeekId = weekId,
            SeriesId = seriesId,
            PercentileRank = percentile.PercentileRank,
            TopSharePercent = percentile.TopSharePercent,
            SampleSize = percentile.FieldSize,
            ComputedAt = Now
        }), ct);
        return percentile;
    }

    public async Task<PrivateCleanupStatus> InspectUserAsync(Guid userId, CancellationToken ct = default)
    {
        var operations = await db.DriverLifecycleOperations.AsNoTracking().Where(o => db.DriverAuthorizationGrants
            .Any(g => g.Id == o.GrantId && g.UserId == userId)).ToListAsync(ct);
        var deletionAt = operations.Where(o => o.Kind == DriverLifecycleKind.DeleteUser)
            .Select(o => (DateTimeOffset?)o.OriginalLossAt).Min();
        var markers = db.EvidenceCopyMarkers.Where(c => c.VerifiedRemovedAt == null && c.UnavailableAt != null
            && db.EvidencePurposes.Any(p => p.Id == c.PurposeId && db.DriverAuthorizationGrants.Any(g => g.Id == p.GrantId && g.UserId == userId)));
        var retained = await markers.CountAsync(ct);
        var tracked = db.DriverTrackedCopies.Where(c => c.UnavailableAt != null
            && db.DriverAuthorizationGrants.Any(g => g.Id == c.GrantId && g.UserId == userId));
        retained += await tracked.CountAsync(ct);
        var trackedDue = db.DriverCopyCleanups.Where(w => w.VerifiedRemovedAt == null && tracked.Any(c =>
            c.GrantId == w.GrantId && c.Purpose == w.Purpose && c.Revision <= w.ThroughRevision));
        var pendingUser = deletionAt is not null && await db.Users.AnyAsync(u => u.Id == userId, ct);
        var dueAt = await markers.Select(c => c.RemovalDueAt).MinAsync(ct);
        var opaqueDueAt = await trackedDue.Select(w => (DateTimeOffset?)w.DueAt).MinAsync(ct);
        if (opaqueDueAt is { } opaque) dueAt = dueAt is { } typed ? EvidenceRetention.Earliest(typed, opaque) : opaque;
        if (deletionAt is not null) dueAt = dueAt is { } earlier ? EvidenceRetention.Earliest(earlier, deletionAt.Value.AddDays(7)) : deletionAt.Value.AddDays(7);
        // User deletion drains every historical association. Its completion supersedes the
        // transport status of an older withdrawal without rewriting that operation's history.
        var required = deletionAt is null ? operations : operations.Where(o => o.Kind == DriverLifecycleKind.DeleteUser).ToList();
        var overdueTracked = await tracked.CountAsync(c => trackedDue.Any(w => w.GrantId == c.GrantId
            && w.Purpose == c.Purpose && c.Revision <= w.ThroughRevision && w.DueAt < Now), ct);
        return new(required.Count != 0 && required.All(o => o.CompletedAt != null), retained + (pendingUser ? 1 : 0),
            await markers.CountAsync(c => c.RemovalDueAt < Now, ct) + overdueTracked + (pendingUser && deletionAt!.Value.AddDays(7) < Now ? 1 : 0),
            operations.Count != 0 && retained == 0 && !pendingUser, dueAt, deletionAt?.AddDays(14));
    }

    internal static async Task RestoreAsync(AppDbContext db, DriverAuthorizationGrant grant, TimeProvider clock, CancellationToken ct)
    {
        var now = DriverAuthorizationPolicy.DurableTime(clock.GetUtcNow());
        var retained = await (from s in db.PrivateUploadSessions
                              join c in db.EvidenceCopyMarkers on s.EvidenceCopyId equals c.Id
                              join p in db.EvidencePurposes on c.PurposeId equals p.Id
                              where p.GrantId == grant.Id && p.OriginalEndedAt != null && c.UnavailableAt == p.OriginalEndedAt
                                  && c.UnavailableAt > now.AddDays(-90)
                                  && c.VerifiedRemovedAt == null && c.RemovalDueAt > now
                              select new { Session = s, Marker = c }).ToListAsync(ct);
        if (retained.Count == 0) return;
        var purpose = new EvidencePurpose
        {
            Id = Guid.NewGuid(),
            Kind = EvidencePurposeKind.Personal,
            Provenance = grant.Provenance,
            GrantId = grant.Id,
            GrantRevision = grant.Revision,
            CreatedAt = now
        };
        db.EvidencePurposes.Add(purpose);
        foreach (var copy in retained)
        {
            var marker = new EvidenceCopyMarker
            {
                Id = Guid.NewGuid(),
                PurposeId = purpose.Id,
                Kind = EvidenceCopyKind.PrivateUpload,
                Provenance = grant.Provenance,
                Generation = 1,
                Version = copy.Marker.Version + 1,
                KeyHash = copy.Marker.KeyHash,
                OriginalAcquiredAt = copy.Marker.OriginalAcquiredAt
            };
            db.EvidenceCopyMarkers.Add(marker);
            copy.Session.EvidenceCopyId = marker.Id;
            copy.Marker.VerifiedRemovedAt = now; // Old generation no longer has a physical row; its clock remains immutable.
        }
    }

    private async Task CheckOwnerAsync(DriverAccess owner, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql() || db.Provenance != DataProvenance.Demo || owner.Scope.Provenance != DataProvenance.Demo
            || owner.Purpose != DriverConsentScope.Personal || !(await journal.ReadCurrentAsync(owner.Scope, ct)).Allows(owner.Revision, owner.Purpose)
            || await new DriverAuthorityStore(db, clock).ResolveAsync(owner.Scope, owner.Purpose, ct) is not { } current
            || current.GrantId != owner.GrantId || current.Revision != owner.Revision) throw new EvidenceCopyUnavailableException();
    }

    private async Task LockAsync(CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) throw new EvidenceCopyUnavailableException();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({370L})", ct);
    }
    private DateTimeOffset Now => DriverAuthorizationPolicy.DurableTime(clock.GetUtcNow());
}
