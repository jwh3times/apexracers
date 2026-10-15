using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Core.PercentileHistory;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Seeder.Demo;

/// <summary>Fresh classified Race-only Demo snapshots from the actual synthetic Subject Driver's
/// retained Field. User claims and old percentile rows are never inputs. Missing evidence stays absent.</summary>
public sealed class DemoPercentileSeeder(AppDbContext db)
{
    public async Task<int> SeedAsync(CancellationToken ct = default)
    {
        if (db.Provenance != DataProvenance.Demo) throw new EvidenceCopyUnavailableException();
        // Source corrections can leave an unavailable prior derivative queued for erasure. Finish
        // that work before capturing the new preparation version or its stable identity can collide.
        await new EvidenceCopyLifecycle(db, TimeProvider.System).ReconcileAsync(ct);
        var writer = await SyntheticEvidenceWriter.OpenAsync(db, ct);
        var userIds = await db.Users.Select(u => u.Id).ToListAsync(ct);
        if (userIds.Count == 0) return 0;
        var subjectRows = await db.SubsessionResults.Where(r => r.CustId == DemoData.DriverCustId
                && r.BestLapSeconds > 0 && r.Subsession.WeekId != null
                && r.Subsession.TrackId == r.Subsession.Week!.TrackId)
            .Select(r => new { r.CarId, WeekId = r.Subsession.WeekId!.Value, r.BestLapSeconds }).ToListAsync(ct);
        if (subjectRows.Count == 0) return 0;
        var weekIds = subjectRows.Select(r => r.WeekId).Distinct().ToArray();
        var carIds = subjectRows.Select(r => r.CarId).Distinct().ToArray();
        var weeks = await db.Weeks.Where(w => weekIds.Contains(w.Id))
            .Select(w => new { w.Id, SeriesId = w.Season.SeriesId }).ToDictionaryAsync(w => w.Id, ct);
        var field = await db.SubsessionResults.Where(r => r.Subsession.WeekId != null
                && weekIds.Contains(r.Subsession.WeekId!.Value) && carIds.Contains(r.CarId) && r.BestLapSeconds > 0
                && r.Subsession.TrackId == r.Subsession.Week!.TrackId)
            .Select(r => new { WeekId = r.Subsession.WeekId!.Value, r.CarId, r.CustId, r.BestLapSeconds, r.EvidenceCopyId })
            .ToListAsync(ct);
        var byWeekCar = field.GroupBy(r => (r.WeekId, r.CarId)).ToDictionary(g => g.Key, g => g.ToList());
        var selection = PercentileSelection.FromRequest(false, null);
        var existing = await db.ClassifiedCarPercentileResults.Where(r => r.OwnerKind == PercentileOwnerKind.DemoSubject
                && r.CustomerId == DemoData.DriverCustId && r.SelectionPolicyVersion == selection.PolicyVersion
                && r.SelectionKey == selection.Key && userIds.Contains(r.UserId) && weekIds.Contains(r.WeekId))
            .ToListAsync(ct);
        var existingByKey = existing.ToDictionary(r => (r.UserId, r.CarId, r.SeriesId, r.WeekId));
        var markerIds = existing.Select(r => r.EvidenceCopyId).OfType<Guid>().ToArray();
        var dependencies = await db.EvidenceCopyDependencies.Where(d => markerIds.Contains(d.CopyId))
            .Select(d => new { d.CopyId, d.SourceCopyId, d.SourceVersion }).ToListAsync(ct);
        var sourceIds = field.Select(r => r.EvidenceCopyId).OfType<Guid>().Distinct().ToArray();
        var versions = await db.EvidenceCopyMarkers.Where(c => sourceIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Version, ct);
        var written = 0;
        foreach (var group in subjectRows.GroupBy(r => (r.WeekId, r.CarId)))
        {
            if (!weeks.TryGetValue(group.Key.WeekId, out var week)) continue;
            var rows = byWeekCar[group.Key];
            var others = rows.Where(r => r.CustId != DemoData.DriverCustId).GroupBy(r => r.CustId)
                .Select(g => g.Min(r => r.BestLapSeconds)).ToList();
            var best = group.Min(r => r.BestLapSeconds);
            var rank = FieldPercentile.Rank(best, others);
            var topShare = FieldPercentile.TopSharePercent(best, others);
            var size = FieldPercentile.FieldSize(others);
            var currentSources = rows.Select(r => r.EvidenceCopyId).OfType<Guid>().ToHashSet();
            foreach (var userId in userIds)
            {
                var key = (userId, group.Key.CarId, week.SeriesId, group.Key.WeekId);
                if (existingByKey.TryGetValue(key, out var copy))
                {
                    var oldSources = dependencies.Where(d => d.CopyId == copy.EvidenceCopyId).ToList();
                    if (copy.PercentileRank == rank && copy.TopSharePercent == topShare && copy.SampleSize == size
                        && copy.PersonalBestLapSeconds == best && copy.PersonalBestLapEvidence == LapEvidence.RaceLap
                        && copy.UploadedSessionType is null && oldSources.Count == currentSources.Count
                        && oldSources.All(d => currentSources.Contains(d.SourceCopyId) && versions.GetValueOrDefault(d.SourceCopyId) == d.SourceVersion))
                        continue; // No new derivative or clock when the fresh facts are identical.
                }
                else
                {
                    copy = new ClassifiedCarPercentileResult
                    {
                        Id = Guid.NewGuid(), UserId = userId, CustomerId = checked((int)DemoData.DriverCustId),
                        Provenance = DataProvenance.Demo, OwnerKind = PercentileOwnerKind.DemoSubject,
                        SelectionPolicyVersion = selection.PolicyVersion, SelectionKey = selection.Key,
                        CarId = group.Key.CarId, SeriesId = week.SeriesId, WeekId = group.Key.WeekId
                    };
                    db.ClassifiedCarPercentileResults.Add(copy);
                    existingByKey[key] = copy;
                }
                copy.PercentileRank = rank; copy.TopSharePercent = topShare; copy.SampleSize = size;
                copy.PersonalBestLapSeconds = best; copy.PersonalBestLapEvidence = LapEvidence.RaceLap;
                copy.UploadedSessionType = null; copy.ComputedAt = DateTimeOffset.UtcNow;
                if (db.Entry(copy).State != EntityState.Added) db.Entry(copy).State = EntityState.Modified;
                written++;
            }
        }
        if (written > 0) await writer.SaveChangesAsync(ct);
        return written;
    }
}
