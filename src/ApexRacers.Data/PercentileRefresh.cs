using Npgsql;
using System.Diagnostics;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Core.PercentileHistory;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Data;

/// <summary>One source/preparation snapshot and one coordinated commit for an explicit Series,
/// Week/Car matrix. API callers resolve current Demo feature eligibility before invoking the Demo
/// path; controlled Seeder production deliberately has its separate synthetic policy. Neither path
/// enables Real. Returned data must remain inside protected publication where Personal use requires it.</summary>
public sealed class PercentileRefresh(AppDbContext db, TimeProvider clock, IDriverEnforcementJournal journal)
{
    public Task<PercentileRefreshBatch> RefreshDemoAsync(Guid authenticatedUserId, PercentileSelection selection,
        int seriesId, IReadOnlyCollection<Guid> weekIds, IReadOnlyCollection<int> carIds, CancellationToken ct = default) =>
        RefreshAsync([authenticatedUserId], null, selection, seriesId, weekIds, carIds, false, ct);

    public Task<PercentileRefreshBatch> RefreshSyntheticDemoAsync(IReadOnlyCollection<Guid> userIds, PercentileSelection selection,
        int seriesId, IReadOnlyCollection<Guid> weekIds, IReadOnlyCollection<int> carIds, CancellationToken ct = default) =>
        RefreshAsync(userIds, null, selection, seriesId, weekIds, carIds, true, ct);

    public Task<PercentileRefreshBatch> RefreshPersonalAsync(DriverAccess requestedOwner, PercentileSelection selection,
        int seriesId, IReadOnlyCollection<Guid> weekIds, IReadOnlyCollection<int> carIds, CancellationToken ct = default) =>
        RefreshAsync([requestedOwner.Scope.UserId], requestedOwner, selection, seriesId, weekIds, carIds, false, ct);

    private sealed record WeekInput(Guid Id, int SeasonId, int RaceWeekIndex, int TrackId, DateOnly StartDate, DateTimeOffset? EndTime);
    private sealed record FieldInput(Guid WeekId, int CarId, long CustomerId, double LapSeconds, Guid SourceId, long Version);
    private sealed record UploadInput(Guid SessionId, int CarId, int TrackId, DateTimeOffset RecordedAt,
        LapSessionType Type, double LapSeconds, Guid SourceId, long Version);
    private sealed record Inputs(List<FieldInput> Fields, List<UploadInput> Uploads);
    private sealed record Preparation(Guid PurposeId, long Generation, long Version, DateTimeOffset AcquiredAt);
    private sealed record Cell(Guid WeekId, int CarId, RefreshedPercentile Reading, EvidenceSourceVersion[] Sources);

    private async Task<PercentileRefreshBatch> RefreshAsync(IReadOnlyCollection<Guid> requestedUsers, DriverAccess? owner,
        PercentileSelection selection, int seriesId, IReadOnlyCollection<Guid> requestedWeeks,
        IReadOnlyCollection<int> requestedCars, bool idempotent, CancellationToken ct)
    {
        var wall = Stopwatch.StartNew();
        var users = requestedUsers.Distinct().ToArray();
        var weekIds = requestedWeeks.Distinct().ToArray();
        var cars = requestedCars.Distinct().ToArray();
        if (selection is null || seriesId <= 0 || users.Any(id => id == Guid.Empty)
            || weekIds.Any(id => id == Guid.Empty) || cars.Any(id => id <= 0)) throw new ArgumentException("A valid history batch is required.");
        if (db.Provenance != DataProvenance.Demo) throw new EvidenceCopyUnavailableException();
        if (users.Length == 0 || weekIds.Length == 0 || cars.Length == 0)
            return new([], 0, new(0, 0, 0, wall.Elapsed.TotalMilliseconds));
        if (await db.Users.CountAsync(u => users.Contains(u.Id), ct) != users.Length) throw new EvidenceCopyUnavailableException();
        var copies = new EvidenceCopyLifecycle(db, clock, journal);
        Preparation preparation;
        if (owner is not null)
        {
            var receipt = await new PrivateUploadStore(db, clock, journal).CaptureAsync(owner, ct);
            preparation = new(receipt.PurposeId, receipt.Generation, receipt.PurposeVersion, receipt.OriginalAcquiredAt);
        }
        else
        {
            var purposeId = await copies.OpenSyntheticPurposeAsync(ct: ct);
            var purpose = await db.EvidencePurposes.AsNoTracking().SingleAsync(p => p.Id == purposeId, ct);
            preparation = new(purposeId, purpose.Generation, purpose.EvidenceVersion, Now);
        }
        // Whole-Series catalog windows preserve the next-Week fallback even when a batch omits it.
        var calendar = await db.Weeks.AsNoTracking().Where(w => w.Season.SeriesId == seriesId)
            .Select(w => new WeekInput(w.Id, w.SeasonId, w.RaceWeekIndex, w.TrackId, w.StartDate, w.EndTime)).ToListAsync(ct);
        var weeks = calendar.Where(w => weekIds.Contains(w.Id)).ToArray();
        if (weeks.Length != weekIds.Length) throw new ArgumentException("Every requested Week must belong to this Series.");
        var windows = calendar.GroupBy(w => w.SeasonId).SelectMany(g =>
        {
            var season = RaceWeekWindow.ForSeason(g.Select(w => (w.RaceWeekIndex, w.StartDate, w.EndTime)))
                .ToDictionary(w => w.RaceWeekIndex, w => w.Window);
            return g.Select(w => (w.Id, Window: season[w.RaceWeekIndex]));
        }).ToDictionary(w => w.Id, w => w.Window);
        var before = await ReadInputsAsync(owner, selection, weekIds, cars, weeks, windows, ct);
        var arithmetic = Stopwatch.StartNew();
        var cells = Calculate(before, owner?.Scope.CustomerId ?? checked((int)DemoData.DriverCustId), users[0], selection,
            seriesId, weeks, cars, windows);
        arithmetic.Stop();
        var changed = await CommitAsync(users, owner, selection, seriesId, weeks, cars, windows, preparation, before, cells, idempotent, ct);
        var readings = users.SelectMany(userId => cells.Select(c => c.Reading with { UserId = userId })).ToArray();
        return new(readings, changed, new(owner is null ? 2 : 4,
            2 * (before.Fields.Count + before.Uploads.Count), arithmetic.Elapsed.TotalMilliseconds, wall.Elapsed.TotalMilliseconds));
    }

    private async Task<Inputs> ReadInputsAsync(DriverAccess? owner, PercentileSelection selection, Guid[] weekIds,
        int[] cars, WeekInput[] weeks, Dictionary<Guid, RaceWeekWindow> windows, CancellationToken ct)
    {
        var query = db.SubsessionResults.AsNoTracking().Where(r => r.Subsession.WeekId != null
            && weekIds.Contains(r.Subsession.WeekId.Value) && cars.Contains(r.CarId) && r.BestLapSeconds > 0
            && r.Subsession.TrackId == r.Subsession.Week!.TrackId);
        if (owner is not null) query = query.Where(r => r.Subsession.OfficialSession);
        var field = await (from r in query join marker in db.EvidenceCopyMarkers.AsNoTracking() on r.EvidenceCopyId equals marker.Id
            select new FieldInput(r.Subsession.WeekId!.Value, r.CarId, r.CustId, r.BestLapSeconds, marker.Id, marker.Version)).ToListAsync(ct);
        List<UploadInput> uploads = [];
        if (owner is not null)
        {
            var tracks = weeks.Select(w => w.TrackId).Distinct().ToArray();
            var types = selection.IncludedTypes.ToArray();
            var start = weeks.Min(w => windows[w.Id].Start);
            var end = weeks.Max(w => windows[w.Id].End);
            uploads = await (from session in new PrivateUploadStore(db, clock, journal).AvailableSessions(owner)
                join lap in db.PrivateUploadedLaps.AsNoTracking() on session.Id equals lap.SessionId
                join marker in db.EvidenceCopyMarkers.AsNoTracking() on session.EvidenceCopyId equals marker.Id
                where cars.Contains(session.CarId) && tracks.Contains(session.TrackId) && types.Contains(session.SessionType)
                    && session.RecordedAt >= start && session.RecordedAt < end
                select new UploadInput(session.Id, session.CarId, session.TrackId, session.RecordedAt,
                    session.SessionType, lap.LapTimeSeconds, marker.Id, marker.Version)).ToListAsync(ct);
        }
        return new(field, uploads);
    }

    private Cell[] Calculate(Inputs inputs, int customerId, Guid userId, PercentileSelection selection, int seriesId,
        WeekInput[] weeks, int[] cars, Dictionary<Guid, RaceWeekWindow> windows)
    {
        var fields = inputs.Fields.GroupBy(r => (r.WeekId, r.CarId)).ToDictionary(g => g.Key, g => g.ToArray());
        var uploadGroups = inputs.Uploads.GroupBy(r => (r.TrackId, r.CarId)).ToDictionary(g => g.Key, g => g.ToArray());
        var output = new List<Cell>();
        foreach (var week in weeks)
        foreach (var carId in cars)
        {
            var rows = fields.GetValueOrDefault((week.Id, carId)) ?? [];
            var byDriver = rows.GroupBy(r => r.CustomerId).ToDictionary(g => g.Key, g => g.Min(r => r.LapSeconds));
            var uploaded = (uploadGroups.GetValueOrDefault((week.TrackId, carId)) ?? [])
                .Where(r => windows[week.Id].Contains(r.RecordedAt)).ToArray();
            var winning = uploaded.OrderBy(r => r.LapSeconds).ThenBy(r => r.SessionId).FirstOrDefault();
            var best = PersonalBest.Select(byDriver.TryGetValue(customerId, out var race) ? race : (double?)null, winning?.LapSeconds);
            var other = byDriver.Where(d => d.Key != customerId).Select(d => d.Value).Order().ToArray();
            // No official Field means no reading even if a private lap survives; no private-only
            // dependency is invented to fill missing independent evidence.
            if (rows.Length == 0) best = null;
            var size = best is null ? (int?)null : FieldPercentile.FieldSize(other);
            var reading = new RefreshedPercentile(userId, customerId, week.Id, carId, seriesId, best,
                best?.Evidence == LapEvidence.UploadedLap ? winning!.Type : null, Array.AsReadOnly(other),
                best is { } pb ? FieldPercentile.Rank(pb.LapSeconds, other) : null,
                best is { } position ? FieldPercentile.Position(position.LapSeconds, other) : null,
                best is { } top ? FieldPercentile.TopSharePercent(top.LapSeconds, other) : null,
                size, size is { } count && FieldPercentile.IsPresentable(count), Now, selection, week.StartDate);
            output.Add(new(week.Id, carId, reading, rows.Select(r => new EvidenceSourceVersion(r.SourceId, r.Version))
                .Concat(uploaded.Select(r => new EvidenceSourceVersion(r.SourceId, r.Version))).Distinct().ToArray()));
        }
        return output.ToArray();
    }

    private async Task<int> CommitAsync(Guid[] users, DriverAccess? owner, PercentileSelection selection, int seriesId,
        WeekInput[] weeks, int[] cars, Dictionary<Guid, RaceWeekWindow> windows, Preparation preparation,
        Inputs before, Cell[] cells, bool idempotent, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            if (db.Database.IsNpgsql())
            {
                // Ordinary copy writers already own 370 before updating catalog/weather. Acquire
                // that coordination first, then fail closed if an independent catalog writer owns
                // a table mutation lock; waiting here could invert its weather-trigger lock order.
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({370L})", ct);
                await db.Database.ExecuteSqlRawAsync("LOCK TABLE iracing.\"Seasons\", iracing.\"Weeks\" IN SHARE MODE NOWAIT", ct);
            }
            else if (db.Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite") throw new EvidenceCopyUnavailableException();
            var copies = new EvidenceCopyLifecycle(db, clock, journal);
            var purpose = await copies.CurrentPurposeAsync(preparation.PurposeId, EvidenceCopyKind.PersonalDerivative, true, ct);
            if (purpose.Generation != preparation.Generation || purpose.EvidenceVersion != preparation.Version
                || purpose.Kind != (owner is null ? EvidencePurposeKind.SyntheticPreview : EvidencePurposeKind.Personal)
                || owner is not null && (purpose.GrantId != owner.GrantId || purpose.GrantRevision != owner.Revision)
                || await db.Users.CountAsync(u => users.Contains(u.Id), ct) != users.Length)
                throw new EvidenceCopyUnavailableException();
            var weekIds = weeks.Select(w => w.Id).ToArray();
            var calendar = await db.Weeks.AsNoTracking().Where(w => w.Season.SeriesId == seriesId)
                .Select(w => new WeekInput(w.Id, w.SeasonId, w.RaceWeekIndex, w.TrackId, w.StartDate, w.EndTime)).ToListAsync(ct);
            if (!weeks.OrderBy(w => w.Id).SequenceEqual(calendar.Where(w => weekIds.Contains(w.Id)).OrderBy(w => w.Id)))
                throw new EvidenceCopyUnavailableException();
            var currentWindows = calendar.GroupBy(w => w.SeasonId).SelectMany(g =>
            {
                var season = RaceWeekWindow.ForSeason(g.Select(w => (w.RaceWeekIndex, w.StartDate, w.EndTime)))
                    .ToDictionary(w => w.RaceWeekIndex, w => w.Window);
                return g.Select(w => (w.Id, Window: season[w.RaceWeekIndex]));
            }).ToDictionary(w => w.Id, w => w.Window);
            if (weeks.Any(w => windows[w.Id] != currentWindows[w.Id])) throw new EvidenceCopyUnavailableException();
            // Re-read ALL current eligible inputs once under coordination, including every empty
            // target. New Fields/uploads and removals cannot be hidden by filtering to old source IDs.
            var current = await ReadInputsAsync(owner, selection, weekIds, cars, weeks, windows, ct);
            if (!SameInputs(before, current)) throw new EvidenceCopyUnavailableException();
            var sources = cells.SelectMany(c => c.Sources).Distinct().ToArray();
            await copies.CheckSourcesAsync(sources, purpose, ct);
            var kind = owner is null ? PercentileOwnerKind.DemoSubject : PercentileOwnerKind.Personal;
            var customerId = owner?.Scope.CustomerId ?? checked((int)DemoData.DriverCustId);
            var grantId = owner?.GrantId;
            var oldRows = await db.ClassifiedCarPercentileResults.IgnoreQueryFilters().AsNoTracking()
                .Where(r => users.Contains(r.UserId) && r.CustomerId == customerId && r.Provenance == db.Provenance
                    && r.OwnerKind == kind && r.GrantId == grantId && r.SelectionPolicyVersion == selection.PolicyVersion
                    && r.SelectionKey == selection.Key && r.SeriesId == seriesId && weekIds.Contains(r.WeekId) && cars.Contains(r.CarId))
                .ToListAsync(ct);
            var oldByKey = oldRows.ToDictionary(r => (r.UserId, r.WeekId, r.CarId));
            var oldIds = oldRows.Select(r => r.EvidenceCopyId).OfType<Guid>().ToArray();
            var oldMarkers = await db.EvidenceCopyMarkers.AsNoTracking().Where(m => oldIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, ct);
            var dependencies = await db.EvidenceCopyDependencies.AsNoTracking().Where(d => oldIds.Contains(d.CopyId))
                .Select(d => new { d.CopyId, d.SourceCopyId, d.SourceVersion }).ToListAsync(ct);
            var targetKeys = users.SelectMany(user => cells.Select(cell =>
                PercentileHistoryKey.Hash(PercentileHistoryKey.Create(new(user, customerId, db.Provenance, kind), grantId,
                    selection, cell.CarId, seriesId, cell.WeekId)))).ToArray();
            var versions = await db.EvidenceCopyMarkers.AsNoTracking().Where(m => m.PurposeId == purpose.Id
                    && m.Kind == EvidenceCopyKind.PersonalDerivative && targetKeys.Contains(m.KeyHash))
                .GroupBy(m => m.KeyHash).Select(g => new { Key = g.Key, Version = g.Max(m => m.Version) })
                .ToDictionaryAsync(m => m.Key, m => m.Version, ct);
            var removed = new List<EvidenceCopyMarker>();
            var additions = new List<(ClassifiedCarPercentileResult Copy, EvidenceCopyMarker Marker, EvidenceSourceVersion[] Sources)>();
            var changed = 0;
            foreach (var user in users)
            foreach (var cell in cells)
            {
                oldByKey.TryGetValue((user, cell.WeekId, cell.CarId), out var old);
                var eligible = cell.Reading.PersonalBest is not null && cell.Reading.IsPresentable;
                var oldMarker = old?.EvidenceCopyId is { } oldId ? oldMarkers.GetValueOrDefault(oldId) : null;
                if (idempotent && eligible && old is not null && oldMarker is { UnavailableAt: null, VerifiedRemovedAt: null }
                    && oldMarker.PurposeId == purpose.Id && oldMarker.Generation == purpose.Generation
                    && old.GrantRevision == owner?.Revision && old.PersonalBestLapSeconds == cell.Reading.PersonalBest!.Value.LapSeconds
                    && old.PersonalBestLapEvidence == cell.Reading.PersonalBest.Value.Evidence
                    && old.UploadedSessionType == cell.Reading.UploadedSessionType && old.SampleSize == cell.Reading.FieldSize
                    && old.PercentileRank == cell.Reading.PercentileRank && old.TopSharePercent == cell.Reading.TopSharePercent
                    && cell.Sources.ToHashSet().SetEquals(dependencies.Where(d => d.CopyId == oldMarker.Id)
                        .Select(d => new EvidenceSourceVersion(d.SourceCopyId, d.SourceVersion)))) continue;
                if (old is null && !eligible) continue;
                changed++;
                if (old is not null)
                {
                    if (oldMarker is null) throw new EvidenceCopyUnavailableException();
                    EvidenceCopyLifecycle.MarkUnavailable(oldMarker, Now, Now);
                    removed.Add(oldMarker);
                }
                if (!eligible) continue;
                var copy = new ClassifiedCarPercentileResult
                {
                    Id = Guid.NewGuid(), UserId = user, CustomerId = customerId, Provenance = db.Provenance, OwnerKind = kind,
                    GrantId = grantId, GrantRevision = owner?.Revision, SelectionPolicyVersion = selection.PolicyVersion,
                    SelectionKey = selection.Key, CarId = cell.CarId, WeekId = cell.WeekId, SeriesId = seriesId,
                    PercentileRank = cell.Reading.PercentileRank!.Value, TopSharePercent = cell.Reading.TopSharePercent!.Value,
                    SampleSize = cell.Reading.FieldSize!.Value, PersonalBestLapSeconds = cell.Reading.PersonalBest!.Value.LapSeconds,
                    PersonalBestLapEvidence = cell.Reading.PersonalBest.Value.Evidence, UploadedSessionType = cell.Reading.UploadedSessionType,
                    ComputedAt = cell.Reading.ComputedAt, PreparationPurposeId = purpose.Id,
                    PreparationGeneration = preparation.Generation, PreparationVersion = preparation.Version
                };
                var key = PercentileHistoryKey.Hash(ClassifiedPercentileContract.Key(copy));
                var marker = new EvidenceCopyMarker { Id = Guid.NewGuid(), PurposeId = purpose.Id, Generation = preparation.Generation,
                    Provenance = db.Provenance, Kind = EvidenceCopyKind.PersonalDerivative, KeyHash = key,
                    Version = versions.GetValueOrDefault(key) + 1, OriginalAcquiredAt = preparation.AcquiredAt };
                copy.EvidenceCopyId = marker.Id; copy.ReadingRevision = marker.Version;
                additions.Add((copy, marker, cell.Sources));
            }
            if (changed == 0) { await transaction.CommitAsync(ct); return 0; }
            if (removed.Count > 0)
            {
                var tracked = db.ChangeTracker.Entries<EvidenceCopyMarker>().ToDictionary(e => e.Entity.Id);
                foreach (var marker in removed)
                {
                    if (tracked.TryGetValue(marker.Id, out var entry)) entry.CurrentValues.SetValues(marker);
                    else db.EvidenceCopyMarkers.Attach(marker).State = EntityState.Modified;
                }
                var ids = removed.Select(m => m.Id).ToArray();
                await copies.InvalidateDependentsAsync(ids, Now, ct);
                await db.SaveChangesAsync(ct); // Original loss metadata precedes physical DELETE fences.
                await copies.RemovePhysicalAsync(ids, ct);
                foreach (var entry in db.ChangeTracker.Entries<ClassifiedCarPercentileResult>().Where(e => ids.Contains(e.Entity.EvidenceCopyId ?? Guid.Empty)).ToArray())
                    entry.State = EntityState.Detached;
                foreach (var marker in removed)
                {
                    var attached = db.EvidenceCopyMarkers.Local.Single(m => m.Id == marker.Id);
                    attached.VerifiedRemovedAt = Now;
                }
            }
            // SQL physical-delete fences may independently advance the purpose. Reload, then one
            // logical application advance, keeping EVERY reading stamped with the original receipt.
            await db.Entry(purpose).ReloadAsync(ct);
            purpose.EvidenceVersion++;
            foreach (var (copy, marker, dependenciesForCopy) in additions)
            {
                db.EvidenceCopyMarkers.Add(marker);
                db.ClassifiedCarPercentileResults.Add(copy);
                db.EvidenceCopyDependencies.AddRange(dependenciesForCopy.Select(s => new EvidenceCopyDependency
                    { CopyId = marker.Id, SourceCopyId = s.CopyId, SourceVersion = s.Version }));
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return changed;
        }
        catch (PostgresException ex) when (ex.SqlState == "55P03")
        { db.ChangeTracker.Clear(); throw new EvidenceCopyUnavailableException(); }
        catch { db.ChangeTracker.Clear(); throw; }
    }

    private static bool SameInputs(Inputs left, Inputs right) =>
        left.Fields.OrderBy(r => r.SourceId).ThenBy(r => r.WeekId).ThenBy(r => r.CarId).ThenBy(r => r.CustomerId).ThenBy(r => r.LapSeconds)
            .SequenceEqual(right.Fields.OrderBy(r => r.SourceId).ThenBy(r => r.WeekId).ThenBy(r => r.CarId).ThenBy(r => r.CustomerId).ThenBy(r => r.LapSeconds))
        && left.Uploads.OrderBy(r => r.SourceId).ThenBy(r => r.SessionId).ThenBy(r => r.LapSeconds)
            .SequenceEqual(right.Uploads.OrderBy(r => r.SourceId).ThenBy(r => r.SessionId).ThenBy(r => r.LapSeconds));
    private DateTimeOffset Now => DriverAuthorizationPolicy.DurableTime(clock.GetUtcNow());
}
