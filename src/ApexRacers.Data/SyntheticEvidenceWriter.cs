using System.Security.Cryptography;
using System.Text;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace ApexRacers.Data;

/// <summary>One explicitly synthetic preparation session, captured before seeding or a Demo mutation.
/// Saves use the same durable purpose/versions/source markers as closed evidence batches.
/// A session never reopens a terminated preview, retries acquisition, or approves Real data.</summary>
public sealed class SyntheticEvidenceWriter
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;
    private readonly long _generation;
    private readonly Guid _purposeId;
    private long _version;
    private readonly DateTimeOffset _openedAt;
    private readonly Dictionary<Guid, long> _sources;
    private readonly Dictionary<(Guid WeekId, int CarId), HashSet<Guid>> _fieldSources;
    private bool _failed;

    private SyntheticEvidenceWriter(AppDbContext db, TimeProvider clock, EvidencePurpose purpose,
        Dictionary<Guid, long> sources, Dictionary<(Guid, int), HashSet<Guid>> fieldSources)
    {
        _db = db;
        _clock = clock;
        _generation = purpose.Generation;
        _purposeId = purpose.Id;
        _version = purpose.EvidenceVersion;
        _openedAt = DriverAuthorizationPolicy.DurableTime(clock.GetUtcNow());
        _sources = sources;
        _fieldSources = fieldSources;
    }

    public static async Task<SyntheticEvidenceWriter> OpenAsync(AppDbContext db, CancellationToken ct = default,
        TimeProvider? clock = null)
    {
        clock ??= TimeProvider.System;
        var lifecycle = new EvidenceCopyLifecycle(db, clock);
        var id = await lifecycle.OpenSyntheticPurposeAsync(ct: ct);
        var purpose = await db.EvidencePurposes.AsNoTracking().SingleAsync(p => p.Id == id, ct);
        var candidates = await db.EvidenceCopyMarkers.AsNoTracking().Where(c => c.Provenance == DataProvenance.Demo
            && c.UnavailableAt == null && c.VerifiedRemovedAt == null
            && db.EvidencePurposes.Any(p => p.Id == c.PurposeId && p.Generation == c.Generation && p.OriginalEndedAt == null
                && (p.Id == id || p.Kind == EvidencePurposeKind.IndependentOfficial || p.Kind == EvidencePurposeKind.AuthorizedHistory)))
            .Select(c => new { c.Id, c.Version, c.ExpiresAt }).ToListAsync(ct);
        var sources = candidates.Where(c => c.ExpiresAt is null || c.ExpiresAt > clock.GetUtcNow()).ToDictionary(c => c.Id, c => c.Version);
        var fields = await db.SubsessionResults.Where(r => r.Subsession.WeekId != null && r.EvidenceCopyId != null)
            .Select(r => new { WeekId = r.Subsession.WeekId!.Value, r.CarId, CopyId = r.EvidenceCopyId!.Value }).Distinct().ToListAsync(ct);
        var fieldSources = fields.GroupBy(r => (r.WeekId, r.CarId)).ToDictionary(g => g.Key, g => g.Select(r => r.CopyId).ToHashSet());
        return new(db, clock, purpose, sources, fieldSources);
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        if (_failed) throw new EvidenceCopyUnavailableException();
        _db.ChangeTracker.DetectChanges();
        var managed = _db.ChangeTracker.Entries<IManagedEvidence>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray();
        var weather = _db.ChangeTracker.Entries<Week>().Where(e =>
            e.Property(w => w.DemoWeatherSummaryJson).IsModified
            || e.State == EntityState.Added && e.Entity.DemoWeatherSummaryJson is not null).ToArray();
        if (managed.Length == 0 && weather.Length == 0) return await _db.SaveChangesAsync(ct);
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            if (_db.Database.IsNpgsql())
                await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({370L})", ct);
            var purpose = await _db.EvidencePurposes.SingleAsync(p => p.Id == _purposeId, ct);
            await _db.Entry(purpose).ReloadAsync(ct);
            if (_db.Provenance != DataProvenance.Demo || purpose.Provenance != DataProvenance.Demo
                || purpose.OriginalEndedAt is not null || purpose.Generation != _generation || purpose.EvidenceVersion != _version)
                throw new EvidenceCopyUnavailableException();
            var lifecycle = new EvidenceCopyLifecycle(_db, _clock);
            foreach (var oldId in managed.Select(e => e.Entity.EvidenceCopyId).Concat(weather.Select(e => e.Entity.DemoWeatherEvidenceCopyId))
                         .OfType<Guid>().Distinct())
            {
                var old = _db.EvidenceCopyMarkers.Local.SingleOrDefault(c => c.Id == oldId)
                    ?? await _db.EvidenceCopyMarkers.SingleAsync(c => c.Id == oldId, ct);
                EvidenceCopyLifecycle.MarkUnavailable(old, Now, Now);
                await lifecycle.InvalidateDependentsAsync(old.Id, Now, ct);
                _sources.Remove(old.Id);
            }
            var copies = managed.Where(e => e.State != EntityState.Deleted).GroupBy(e => Identity(e.Entity))
                .OrderBy(g => g.Key.Item1 == EvidenceCopyKind.PersonalDerivative ? 1 : 0);
            foreach (var group in copies)
            {
                var (kind, key) = group.Key;
                if (kind == EvidenceCopyKind.OfficialField && group.All(e => e.Entity is not Subsession))
                    throw new EvidenceCopyUnavailableException(); // A Field is persisted as one batch, including its header.
                var marker = await MarkerAsync(purpose, kind, key, ct);
                foreach (var entry in group)
                {
                    if (entry.Entity.Provenance is not (DataProvenance.Unknown or DataProvenance.Demo))
                        throw new EvidenceCopyUnavailableException();
                    entry.Entity.Provenance = DataProvenance.Demo;
                    entry.Entity.EvidenceCopyId = marker.Id;
                    if (entry.Entity is ExternalDataCache cache)
                    {
                        // A new collection uses its preparation clock; a retry cannot renew that clock.
                        cache.FetchedAt = _openedAt;
                        marker.ExpiresAt = cache.ExpiresAt;
                        marker.RemovalDueAt = EvidenceRetention.MappedRemovalDueAt(cache.ExpiresAt);
                    }
                }
                if (kind == EvidenceCopyKind.PersonalDerivative)
                {
                    var result = (CarPercentileResult)group.Single().Entity;
                    var sourceIds = await _db.SubsessionResults.Where(r => r.CarId == result.CarId && r.Subsession.WeekId == result.WeekId)
                        .Select(r => r.EvidenceCopyId).Distinct().ToListAsync(ct);
                    var stagedFields = managed.Where(e => e.Entity is Subsession s && s.WeekId == result.WeekId)
                        .Select(e => e.Entity.EvidenceCopyId).ToArray();
                    var captured = _fieldSources.GetValueOrDefault((result.WeekId, result.CarId)) ?? [];
                    var current = sourceIds.OfType<Guid>().ToHashSet();
                    if (!captured.SetEquals(current)) throw new EvidenceCopyUnavailableException();
                    var actualSources = captured.Concat(stagedFields.OfType<Guid>()).Distinct().ToArray();
                    var retainedSources = new List<EvidenceSourceVersion>();
                    foreach (var sourceId in actualSources)
                    {
                        var staged = _db.EvidenceCopyMarkers.Local.SingleOrDefault(c => c.Id == sourceId
                            && _db.Entry(c).State == EntityState.Added && c.Kind == EvidenceCopyKind.OfficialField);
                        if (staged is not null)
                        {
                            _db.EvidenceCopyDependencies.Add(new() { CopyId = marker.Id, SourceCopyId = sourceId, SourceVersion = staged.Version });
                            continue;
                        }
                        if (!_sources.TryGetValue(sourceId, out var sourceVersion))
                            throw new EvidenceCopyUnavailableException();
                        retainedSources.Add(new(sourceId, sourceVersion));
                        _db.EvidenceCopyDependencies.Add(new() { CopyId = marker.Id, SourceCopyId = sourceId, SourceVersion = sourceVersion });
                    }
                    await lifecycle.CheckSourcesAsync(retainedSources, purpose, ct);
                    if (actualSources.Length == 0) throw new EvidenceCopyUnavailableException();
                }
                _sources[marker.Id] = marker.Version;
            }
            foreach (var entry in weather)
            {
                if (entry.Entity.DemoWeatherSummaryJson is null) { entry.Entity.DemoWeatherEvidenceCopyId = null; continue; }
                var marker = await MarkerAsync(purpose, EvidenceCopyKind.Weather, $"weather:{entry.Entity.Id}", ct);
                entry.Entity.DemoWeatherEvidenceCopyId = marker.Id;
            }
            purpose.EvidenceVersion++;
            var saved = await _db.SaveChangesAsync(ct);
            await _db.Entry(purpose).ReloadAsync(ct);
            var savedFields = await _db.SubsessionResults.Where(r => r.Subsession.WeekId != null && r.EvidenceCopyId != null)
                .Select(r => new { WeekId = r.Subsession.WeekId!.Value, r.CarId, CopyId = r.EvidenceCopyId!.Value }).Distinct().ToListAsync(ct);
            await tx.CommitAsync(ct);
            _version = purpose.EvidenceVersion;
            _fieldSources.Clear();
            foreach (var group in savedFields.GroupBy(r => (r.WeekId, r.CarId)))
                _fieldSources[group.Key] = group.Select(r => r.CopyId).ToHashSet();
            return saved;
        }
        catch { _failed = true; _db.ChangeTracker.Clear(); throw; }
    }

    private async Task<EvidenceCopyMarker> MarkerAsync(EvidencePurpose purpose, EvidenceCopyKind kind, string key, CancellationToken ct)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        var version = await _db.EvidenceCopyMarkers.Where(c => c.PurposeId == purpose.Id && c.Kind == kind && c.KeyHash == hash)
            .Select(c => (long?)c.Version).MaxAsync(ct) ?? 0;
        var marker = new EvidenceCopyMarker
        {
            Id = Guid.NewGuid(),
            PurposeId = purpose.Id,
            Generation = _generation,
            Provenance = DataProvenance.Demo,
            Kind = kind,
            KeyHash = hash,
            Version = version + 1,
            OriginalAcquiredAt = _openedAt
        };
        _db.EvidenceCopyMarkers.Add(marker);
        return marker;
    }

    private static (EvidenceCopyKind, string) Identity(IManagedEvidence evidence) => evidence switch
    {
        ExternalDataCache c => (EvidenceCopyKind.MappedCache, c.CacheKey),
        Subsession c => (EvidenceCopyKind.OfficialField, $"field:{c.Id}"),
        SubsessionResult c => (EvidenceCopyKind.OfficialField, $"field:{c.SubsessionId}"),
        SeasonCarBop c => (EvidenceCopyKind.Bop, $"bop:{c.SeasonId}:{c.RaceWeekIndex}:{c.CarId}"),
        CarPercentileResult c => (EvidenceCopyKind.PersonalDerivative, $"percentile:{c.Id}"),
        Rival c => (EvidenceCopyKind.Follow, $"follow:{c.Id}"),
        _ => throw new EvidenceCopyUnavailableException(),
    };
    private DateTimeOffset Now => DriverAuthorizationPolicy.DurableTime(_clock.GetUtcNow());
}
