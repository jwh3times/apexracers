using Npgsql;
using System.Diagnostics;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Core.PercentileHistory;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Data;

/// <summary>One source/preparation snapshot and one coordinated commit for an explicit Week/Car
/// matrix or complete discovered history. API callers resolve Demo feature eligibility before invoking the Demo
/// path; controlled Seeder production deliberately has its separate synthetic policy. Neither path
/// enables Real. Returned data must remain inside protected publication where Personal use requires it.</summary>
public sealed class PercentileRefresh(AppDbContext db, TimeProvider clock, IDriverEnforcementJournal journal)
{
    public async Task<PercentileRefreshBatch> RefreshDemoAsync(Guid authenticatedUserId, PercentileSelection selection,
        int seriesId, IReadOnlyCollection<Guid> weekIds, IReadOnlyCollection<int> carIds, CancellationToken ct = default) =>
        (await RefreshAsync([authenticatedUserId], null, selection, seriesId, weekIds, carIds, false, false, ct)).Batch;

    public async Task<PercentileRefreshBatch> RefreshSyntheticDemoAsync(IReadOnlyCollection<Guid> userIds, PercentileSelection selection,
        int seriesId, IReadOnlyCollection<Guid> weekIds, IReadOnlyCollection<int> carIds, CancellationToken ct = default) =>
        (await RefreshAsync(userIds, null, selection, seriesId, weekIds, carIds, true, false, ct)).Batch;

    public async Task<PercentileRefreshBatch> RefreshPersonalAsync(DriverAccess requestedOwner, PercentileSelection selection,
        int seriesId, IReadOnlyCollection<Guid> weekIds, IReadOnlyCollection<int> carIds, CancellationToken ct = default) =>
        (await RefreshAsync([requestedOwner.Scope.UserId], requestedOwner, selection, seriesId, weekIds, carIds, false, false, ct)).Batch;

    // Every Demo User shares one synthetic source purpose. Serialize discovered readers before
    // preparation so their own derived writes cannot invalidate overlapping unchanged inputs.
    // This is separate from 370: retained-source writers can still change inputs and deny commit.
    private const long DemoHistoryReadGate = 428;

    public async Task<PercentileHistorySnapshot> ReadDemoHistoryAsync(Guid userId, PercentileSelection selection,
        int? seriesId = null, CancellationToken ct = default)
    {
        if (!db.Database.IsNpgsql())
            return (await RefreshAsync([userId], null, selection, seriesId, [], [], true, true, ct)).Snapshot!;
        // Keep ONE physical session open across the existing short capture/commit transactions.
        // A second pooled gate connection per waiter could starve the active reader's connection.
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_lock({DemoHistoryReadGate})", ct);
            return (await RefreshAsync([userId], null, selection, seriesId, [], [], true, true, ct)).Snapshot!;
        }
        finally
        {
            try
            {
                // Acquisition can race cancellation: unlock unconditionally with independent time,
                // even when the acquiring statement threw before reporting success to the client.
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_unlock({DemoHistoryReadGate})", cleanup.Token);
            }
            catch
            {
                // EF refcount-close alone may leave a caller-opened physical session alive, and
                // pooled reset can be deferred. Discard this session before reporting failure.
                var connection = (NpgsqlConnection)db.Database.GetDbConnection();
                NpgsqlConnection.ClearPool(connection);
                await connection.CloseAsync();
                throw;
            }
            finally { await db.Database.CloseConnectionAsync(); }
        }
    }

    public async Task<PercentileHistorySnapshot> ReadPersonalHistoryAsync(DriverAccess owner, PercentileSelection selection,
        int? seriesId = null, CancellationToken ct = default) =>
        (await RefreshAsync([owner.Scope.UserId], owner, selection, seriesId, [], [], true, true, ct)).Snapshot!;

    public Task<bool> SnapshotCurrentAsync(PercentileHistorySnapshot snapshot, CancellationToken ct = default) => snapshot.CurrentAsync(ct);

    private sealed record WeekInput(Guid Id, int SeasonId, int SeriesId, string SeriesName, int RaceWeekIndex,
        int TrackId, string TrackName, string? TrackConfigName, DateOnly StartDate, DateTimeOffset? EndTime);
    private sealed record SeriesInput(int Id, string Name);
    private sealed record Catalog(List<SeriesInput> Series, List<PercentileHistorySeasonMetadata> Seasons, List<WeekInput> Weeks);
    private sealed record CarInput(int Id, string Name);
    private sealed record RefreshOutcome(PercentileRefreshBatch Batch, PercentileHistorySnapshot? Snapshot = null);
    private sealed record CommitOutcome(int ChangedRows, long Version, int[]? DiscoveredCars = null);
    private sealed record FieldInput(Guid WeekId, int CarId, long CustomerId, double LapSeconds, Guid SourceId, long Version);
    private sealed record UploadInput(Guid SessionId, int CarId, int TrackId, DateTimeOffset RecordedAt,
        LapSessionType Type, double LapSeconds, Guid SourceId, long Version);
    private sealed record Inputs(List<FieldInput> Fields, List<UploadInput> Uploads);
    private sealed record Preparation(Guid PurposeId, long Generation, long Version, DateTimeOffset AcquiredAt);
    private sealed record Cell(Guid WeekId, int CarId, RefreshedPercentile Reading, EvidenceSourceVersion[] Sources);

    private async Task<RefreshOutcome> RefreshAsync(IReadOnlyCollection<Guid> requestedUsers, DriverAccess? owner,
        PercentileSelection selection, int? seriesId, IReadOnlyCollection<Guid> requestedWeeks,
        IReadOnlyCollection<int> requestedCars, bool idempotent, bool discover, CancellationToken ct)
    {
        var wall = Stopwatch.StartNew();
        var users = requestedUsers.Distinct().ToArray();
        var weekIds = requestedWeeks.Distinct().ToArray();
        var cars = requestedCars.Distinct().ToArray();
        if (selection is null || seriesId is <= 0 || users.Any(id => id == Guid.Empty)
            || weekIds.Any(id => id == Guid.Empty) || cars.Any(id => id <= 0)) throw new ArgumentException("A valid history batch is required.");
        if (db.Provenance != DataProvenance.Demo) throw new EvidenceCopyUnavailableException();
        if (!discover && (users.Length == 0 || weekIds.Length == 0 || cars.Length == 0))
            return new(new([], 0, new(0, 0, 0, wall.Elapsed.TotalMilliseconds)));
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
        var catalog = await ReadCatalogAsync(seriesId, ct);
        var calendar = catalog.Weeks;
        if (discover) weekIds = calendar.Select(w => w.Id).ToArray();
        var weeks = calendar.Where(w => weekIds.Contains(w.Id)).ToArray();
        if (weeks.Length != weekIds.Length) throw new ArgumentException("Every requested Week must belong to this Series.");
        var windows = Windows(calendar);
        if (discover) cars = await DiscoverCarsAsync(users, owner, selection, seriesId, weeks, windows, ct);
        var carNames = discover ? await db.Cars.AsNoTracking().Where(c => cars.Contains(c.Id))
            .Select(c => new CarInput(c.Id, c.Name)).ToListAsync(ct) : [];
        var before = await ReadInputsAsync(owner, selection, weekIds, cars, weeks, windows, ct);
        var arithmetic = Stopwatch.StartNew();
        var cells = Calculate(before, owner?.Scope.CustomerId ?? checked((int)DemoData.DriverCustId), users[0], selection,
            weeks, cars, windows);
        arithmetic.Stop();
        var committed = await CommitAsync(users, owner, selection, seriesId, catalog, carNames, weeks, cars, windows, preparation, before, cells, idempotent, discover, ct);
        var readings = users.SelectMany(userId => cells.Select(c => c.Reading with { UserId = userId })).ToArray();
        var batch = new PercentileRefreshBatch(readings, committed.ChangedRows, new(weekIds.Length == 0 || cars.Length == 0 ? 0 : owner is null ? 2 : 4,
            2 * (before.Fields.Count + before.Uploads.Count), arithmetic.Elapsed.TotalMilliseconds, wall.Elapsed.TotalMilliseconds));
        if (!discover) return new(batch);
        var witness = preparation with { Version = committed.Version };
        var snapshot = new PercentileHistorySnapshot(owner?.Scope.CustomerId ?? checked((int)DemoData.DriverCustId), selection,
            batch.Readings, weeks.Select(w => new PercentileHistoryWeekMetadata(w.Id, w.SeasonId, w.SeriesId, w.SeriesName,
                w.RaceWeekIndex, w.TrackId, w.TrackName, w.TrackConfigName, w.StartDate)), catalog.Seasons,
            carNames.ToDictionary(c => c.Id, c => c.Name), token => CurrentSnapshotAsync(users, owner, selection, seriesId,
                catalog, carNames, weeks, cars, committed.DiscoveredCars ?? cars, windows, witness, before, cells, token));
        return new(batch, snapshot);
    }

    private async Task<Inputs> ReadInputsAsync(DriverAccess? owner, PercentileSelection selection, Guid[] weekIds,
        int[] cars, WeekInput[] weeks, Dictionary<Guid, RaceWeekWindow> windows, CancellationToken ct)
    {
        if (weekIds.Length == 0 || cars.Length == 0) return new([], []);
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

    private Cell[] Calculate(Inputs inputs, int customerId, Guid userId, PercentileSelection selection,
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
            var reading = new RefreshedPercentile(userId, customerId, week.Id, carId, week.SeriesId, best,
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

    private async Task<CommitOutcome> CommitAsync(Guid[] users, DriverAccess? owner, PercentileSelection selection, int? seriesId, Catalog originalCatalog, List<CarInput> carNames,
        WeekInput[] weeks, int[] cars, Dictionary<Guid, RaceWeekWindow> windows, Preparation preparation,
        Inputs before, Cell[] cells, bool idempotent, bool discover, CancellationToken ct)
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
                await db.Database.ExecuteSqlRawAsync("LOCK TABLE iracing.\"Series\", iracing.\"Seasons\", iracing.\"Weeks\", iracing.\"Tracks\", iracing.\"Cars\" IN SHARE MODE NOWAIT", ct);
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
            var currentCatalog = await ReadCatalogAsync(seriesId, ct);
            var calendar = currentCatalog.Weeks;
            if (discover && !SameCatalog(originalCatalog, currentCatalog)) throw new EvidenceCopyUnavailableException();
            if (!weeks.OrderBy(w => w.Id).SequenceEqual(calendar.Where(w => weekIds.Contains(w.Id)).OrderBy(w => w.Id)))
                throw new EvidenceCopyUnavailableException();
            var currentWindows = Windows(calendar);
            if (weeks.Any(w => windows[w.Id] != currentWindows[w.Id])) throw new EvidenceCopyUnavailableException();
            if (discover)
            {
                var discovered = await DiscoverCarsAsync(users, owner, selection, seriesId, weeks, windows, ct);
                if (!cars.ToHashSet().SetEquals(discovered)) throw new EvidenceCopyUnavailableException();
                var names = await db.Cars.AsNoTracking().Where(c => cars.Contains(c.Id)).Select(c => new CarInput(c.Id, c.Name)).ToListAsync(ct);
                if (!carNames.OrderBy(c => c.Id).SequenceEqual(names.OrderBy(c => c.Id))) throw new EvidenceCopyUnavailableException();
            }
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
                    && r.SelectionKey == selection.Key && (seriesId == null || r.SeriesId == seriesId) && weekIds.Contains(r.WeekId) && cars.Contains(r.CarId))
                .ToListAsync(ct);
            var oldByKey = oldRows.ToDictionary(r => (r.UserId, r.WeekId, r.CarId));
            var oldIds = oldRows.Select(r => r.EvidenceCopyId).OfType<Guid>().ToArray();
            var oldMarkers = await db.EvidenceCopyMarkers.AsNoTracking().Where(m => oldIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, ct);
            var dependencies = await db.EvidenceCopyDependencies.AsNoTracking().Where(d => oldIds.Contains(d.CopyId))
                .Select(d => new { d.CopyId, d.SourceCopyId, d.SourceVersion }).ToListAsync(ct);
            var targetKeys = users.SelectMany(user => cells.Select(cell =>
                PercentileHistoryKey.Hash(PercentileHistoryKey.Create(new(user, customerId, db.Provenance, kind), grantId,
                    selection, cell.CarId, cell.Reading.SeriesId, cell.WeekId)))).ToArray();
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
                    removed.Add(oldMarker);
                }
                if (!eligible) continue;
                var copy = new ClassifiedCarPercentileResult
                {
                    Id = Guid.NewGuid(), UserId = user, CustomerId = customerId, Provenance = db.Provenance, OwnerKind = kind,
                    GrantId = grantId, GrantRevision = owner?.Revision, SelectionPolicyVersion = selection.PolicyVersion,
                    SelectionKey = selection.Key, CarId = cell.CarId, WeekId = cell.WeekId, SeriesId = cell.Reading.SeriesId,
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
            if (changed == 0)
            {
                var observedCars = discover ? await DiscoverCarsAsync(users, owner, selection, seriesId, weeks, windows, ct) : null;
                await transaction.CommitAsync(ct); return new(0, purpose.EvidenceVersion, observedCars);
            }
            await copies.RetireReplacedCopiesAsync(removed, ct);
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
            var finalCars = discover ? await DiscoverCarsAsync(users, owner, selection, seriesId, weeks, windows, ct) : null;
            await transaction.CommitAsync(ct);
            return new(changed, purpose.EvidenceVersion, finalCars);
        }
        catch (PostgresException ex) when (ex.SqlState == "55P03")
        { db.ChangeTracker.Clear(); throw new EvidenceCopyUnavailableException(); }
        catch { db.ChangeTracker.Clear(); throw; }
    }

    private async Task<Catalog> ReadCatalogAsync(int? seriesId, CancellationToken ct)
    {
        var series = await db.Series.AsNoTracking().Where(s => seriesId == null || s.Id == seriesId)
            .Select(s => new SeriesInput(s.Id, s.Name)).ToListAsync(ct);
        var seasons = await db.Seasons.AsNoTracking().Where(s => seriesId == null || s.SeriesId == seriesId)
            .Select(s => new PercentileHistorySeasonMetadata(s.Id, s.SeriesId, s.Year, s.Quarter, s.Active)).ToListAsync(ct);
        var weeks = await db.Weeks.AsNoTracking().Where(w => seriesId == null || w.Season.SeriesId == seriesId)
            .Select(w => new WeekInput(w.Id, w.SeasonId, w.Season.SeriesId, w.Season.Series.Name, w.RaceWeekIndex,
                w.TrackId, w.Track.Name, w.Track.ConfigName, w.StartDate, w.EndTime)).ToListAsync(ct);
        return new(series, seasons, weeks);
    }

    private static Dictionary<Guid, RaceWeekWindow> Windows(IEnumerable<WeekInput> calendar) =>
        calendar.GroupBy(w => w.SeasonId).SelectMany(g =>
        {
            var season = RaceWeekWindow.ForSeason(g.Select(w => (w.RaceWeekIndex, w.StartDate, w.EndTime)))
                .ToDictionary(w => w.RaceWeekIndex, w => w.Window);
            return g.Select(w => (w.Id, Window: season[w.RaceWeekIndex]));
        }).ToDictionary(w => w.Id, w => w.Window);

    private async Task<int[]> DiscoverCarsAsync(Guid[] users, DriverAccess? owner, PercentileSelection selection,
        int? seriesId, WeekInput[] weeks, Dictionary<Guid, RaceWeekWindow> windows, CancellationToken ct)
    {
        var ids = weeks.Select(w => w.Id).ToArray();
        var field = db.SubsessionResults.AsNoTracking().Where(r => r.Subsession.WeekId != null
            && ids.Contains(r.Subsession.WeekId.Value) && r.BestLapSeconds > 0 && r.Subsession.TrackId == r.Subsession.Week!.TrackId);
        if (owner is not null) field = field.Where(r => r.Subsession.OfficialSession);
        var fieldCars = await field.Select(r => r.CarId).Distinct().ToListAsync(ct);
        var kind = owner is null ? PercentileOwnerKind.DemoSubject : PercentileOwnerKind.Personal;
        var customerId = owner?.Scope.CustomerId ?? checked((int)DemoData.DriverCustId);
        var grantId = owner?.GrantId;
        var old = await db.ClassifiedCarPercentileResults.IgnoreQueryFilters().AsNoTracking()
            .Where(r => users.Contains(r.UserId) && r.CustomerId == customerId && r.OwnerKind == kind
                && r.Provenance == db.Provenance && r.GrantId == grantId && r.SelectionPolicyVersion == selection.PolicyVersion
                && r.SelectionKey == selection.Key && (seriesId == null || r.SeriesId == seriesId) && ids.Contains(r.WeekId))
            .Select(r => r.CarId).Distinct().ToListAsync(ct);
        var uploadCars = new List<int>();
        if (owner is not null && weeks.Length != 0)
        {
            var types = selection.IncludedTypes.ToArray();
            var tracks = weeks.Select(w => w.TrackId).Distinct().ToArray();
            var start = weeks.Min(w => windows[w.Id].Start); var end = weeks.Max(w => windows[w.Id].End);
            uploadCars = await new PrivateUploadStore(db, clock, journal).AvailableSessions(owner)
                .Where(s => types.Contains(s.SessionType) && tracks.Contains(s.TrackId) && s.RecordedAt >= start && s.RecordedAt < end)
                .Select(s => s.CarId).Distinct().ToListAsync(ct);
        }
        return fieldCars.Concat(old).Concat(uploadCars).Distinct().Order().ToArray();
    }

    private static bool SameCatalog(Catalog left, Catalog right) =>
        left.Series.OrderBy(s => s.Id).SequenceEqual(right.Series.OrderBy(s => s.Id))
        && left.Seasons.OrderBy(s => s.SeasonId).SequenceEqual(right.Seasons.OrderBy(s => s.SeasonId))
        && left.Weeks.OrderBy(w => w.Id).SequenceEqual(right.Weeks.OrderBy(w => w.Id));

    private async Task<bool> CurrentSnapshotAsync(Guid[] users, DriverAccess? owner, PercentileSelection selection,
        int? seriesId, Catalog catalog, List<CarInput> carNames, WeekInput[] weeks, int[] cars, int[] expectedDiscoveryCars,
        Dictionary<Guid, RaceWeekWindow> windows, Preparation preparation, Inputs before, Cell[] cells, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            if (!db.Database.IsNpgsql()) return false; // Protected current-state evidence uses PostgreSQL.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({370L})", ct);
            await db.Database.ExecuteSqlRawAsync("LOCK TABLE iracing.\"Series\", iracing.\"Seasons\", iracing.\"Weeks\", iracing.\"Tracks\", iracing.\"Cars\" IN SHARE MODE NOWAIT", ct);
            var copies = new EvidenceCopyLifecycle(db, clock, journal);
            var purpose = await copies.CurrentPurposeAsync(preparation.PurposeId, EvidenceCopyKind.PersonalDerivative, true, ct);
            if (purpose.Generation != preparation.Generation || purpose.EvidenceVersion != preparation.Version
                || purpose.Kind != (owner is null ? EvidencePurposeKind.SyntheticPreview : EvidencePurposeKind.Personal)
                || owner is not null && (purpose.GrantId != owner.GrantId || purpose.GrantRevision != owner.Revision)
                || await db.Users.CountAsync(u => users.Contains(u.Id), ct) != users.Length) return false;
            if (!SameCatalog(catalog, await ReadCatalogAsync(seriesId, ct))) return false;
            if (!expectedDiscoveryCars.ToHashSet().SetEquals(await DiscoverCarsAsync(users, owner, selection, seriesId, weeks, windows, ct))) return false;
            var names = await db.Cars.AsNoTracking().Where(c => cars.Contains(c.Id)).Select(c => new CarInput(c.Id, c.Name)).ToListAsync(ct);
            if (!carNames.OrderBy(c => c.Id).SequenceEqual(names.OrderBy(c => c.Id))) return false;
            var current = await ReadInputsAsync(owner, selection, weeks.Select(w => w.Id).ToArray(), cars, weeks, windows, ct);
            if (!SameInputs(before, current)) return false;
            await copies.CheckSourcesAsync(cells.SelectMany(c => c.Sources).Distinct().ToArray(), purpose, ct);
            await transaction.CommitAsync(ct);
            return true;
        }
        catch (EvidenceCopyUnavailableException) { return false; }
        catch (PostgresException ex) when (ex.SqlState == "55P03") { return false; }
    }

    private static bool SameInputs(Inputs left, Inputs right) =>
        left.Fields.OrderBy(r => r.SourceId).ThenBy(r => r.WeekId).ThenBy(r => r.CarId).ThenBy(r => r.CustomerId).ThenBy(r => r.LapSeconds)
            .SequenceEqual(right.Fields.OrderBy(r => r.SourceId).ThenBy(r => r.WeekId).ThenBy(r => r.CarId).ThenBy(r => r.CustomerId).ThenBy(r => r.LapSeconds))
        && left.Uploads.OrderBy(r => r.SourceId).ThenBy(r => r.SessionId).ThenBy(r => r.LapSeconds)
            .SequenceEqual(right.Uploads.OrderBy(r => r.SourceId).ThenBy(r => r.SessionId).ThenBy(r => r.LapSeconds));
    private DateTimeOffset Now => DriverAuthorizationPolicy.DurableTime(clock.GetUtcNow());
}
