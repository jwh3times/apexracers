using System.Data.Common;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace ApexRacers.Tests.Services;

[Collection(PostgreSqlCollection.Name)]
public sealed class EvidenceCopyClockTests(PostgreSqlFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RawWeatherRemovalInvalidatesOverdueMappedCopyWithoutExtendingItsDeadline(bool deleteWeek)
    {
        await using var db = await CreateAsync();
        var clock = new CopyClock(TimeProvider.System.GetUtcNow().AddDays(-20));
        var week = await CatalogAsync(db);
        var lifecycle = new EvidenceCopyLifecycle(db, clock);
        var official = await lifecycle.OpenSyntheticPurposeAsync(EvidencePurposeKind.IndependentOfficial, 1, ct: Ct);
        var weather = await lifecycle.CaptureAsync(official, EvidenceCopyKind.Weather, "weather", ct: Ct);
        var source = await lifecycle.CommitAsync(weather, new WeatherBatch(week, "{}"), Ct);
        var preview = await lifecycle.OpenSyntheticPurposeAsync(ct: Ct);
        var receipt = await lifecycle.CaptureAsync(preview, EvidenceCopyKind.MappedCache, "overdue-weather-derived", [source], Ct);
        var payload = Cache(receipt, TimeSpan.FromHours(1));
        var copy = await lifecycle.CommitAsync(receipt, payload, Ct);
        var before = await db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(m => m.Id == copy.CopyId, Ct);
        Assert.Null(before.UnavailableAt);
        Assert.True(before.RemovalDueAt < TimeProvider.System.GetUtcNow());
        clock.Advance(TimeSpan.FromDays(20));

        await db.Database.ExecuteSqlRawAsync(deleteWeek ? "DELETE FROM iracing.\"Weeks\""
            : "UPDATE iracing.\"Weeks\" SET \"DemoWeatherSummaryJson\" = NULL", Ct);

        var after = await db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(m => m.Id == copy.CopyId, Ct);
        Assert.Equal(before.OriginalAcquiredAt, after.OriginalAcquiredAt);
        Assert.Equal(before.ExpiresAt, after.ExpiresAt);
        Assert.Equal(before.ExpiresAt, after.UnavailableAt);
        Assert.Equal(before.RemovalDueAt, after.RemovalDueAt);
        Assert.Empty(await db.ExternalDataCaches.ToListAsync(Ct));
        await lifecycle.ReconcileAsync(Ct);
        Assert.Empty(await db.ExternalDataCaches.IgnoreQueryFilters().ToListAsync(Ct));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplicationReplacementOrPurposeClosurePreservesOverdueCopyClocks(bool replace)
    {
        await using var db = await CreateAsync();
        var clock = new CopyClock(TimeProvider.System.GetUtcNow().AddDays(-20));
        var lifecycle = new EvidenceCopyLifecycle(db, clock);
        var purpose = await lifecycle.OpenSyntheticPurposeAsync(ct: Ct);
        var receipt = await lifecycle.CaptureAsync(purpose, EvidenceCopyKind.MappedCache, "short-lived", ct: Ct);
        var copy = await lifecycle.CommitAsync(receipt, Cache(receipt, TimeSpan.FromHours(1)), Ct);
        var before = await db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(m => m.Id == copy.CopyId, Ct);
        clock.Advance(TimeSpan.FromDays(20));
        var next = await lifecycle.CaptureAsync(purpose, EvidenceCopyKind.MappedCache, "short-lived", ct: Ct);
        if (replace) await lifecycle.CommitAsync(next, Cache(next, TimeSpan.FromDays(1)), Ct);
        else
        {
            await lifecycle.EndPurposeAsync(purpose, clock.GetUtcNow(), Ct);
            clock.Advance(TimeSpan.FromHours(1));
            await lifecycle.EndPurposeAsync(purpose, clock.GetUtcNow(), Ct);
        }
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => lifecycle.CommitAsync(next, Cache(next, TimeSpan.FromDays(1)), Ct));
        var after = await db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(m => m.Id == copy.CopyId, Ct);
        Assert.Equal(before.OriginalAcquiredAt, after.OriginalAcquiredAt);
        Assert.Equal(before.ExpiresAt, after.ExpiresAt);
        Assert.Equal(before.ExpiresAt, after.UnavailableAt);
        Assert.Equal(before.RemovalDueAt, after.RemovalDueAt);
        await lifecycle.ReconcileAsync(Ct);
        await lifecycle.ReconcileAsync(Ct);
        Assert.Equal(replace ? 1 : 0, await db.ExternalDataCaches.IgnoreQueryFilters().CountAsync(Ct));
        Assert.NotNull((await db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(m => m.Id == copy.CopyId, Ct)).VerifiedRemovedAt);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TransitiveLossConvergesAcrossAlternatePathsAndCycles(bool rawRemoval, bool cycle)
    {
        await using var db = await CreateAsync();
        var clock = new CopyClock(TimeProvider.System.GetUtcNow().AddDays(-20));
        var week = await CatalogAsync(db);
        var lifecycle = new EvidenceCopyLifecycle(db, clock);
        var official = await lifecycle.OpenSyntheticPurposeAsync(EvidencePurposeKind.IndependentOfficial, 1, ct: Ct);
        var weather = await lifecycle.CaptureAsync(official, EvidenceCopyKind.Weather, "weather", ct: Ct);
        var source = await lifecycle.CommitAsync(weather, new WeatherBatch(week, "{}"), Ct);
        var preview = await lifecycle.OpenSyntheticPurposeAsync(ct: Ct);
        var early = await CommitMappedAsync(lifecycle, preview, "early", TimeSpan.FromHours(1), source);
        var bridge = await CommitMappedAsync(lifecycle, preview, "bridge", TimeSpan.FromDays(30), early);
        var terminal = await CommitMappedAsync(lifecycle, preview, "terminal", TimeSpan.FromDays(30), source, bridge);
        var leaf = await CommitMappedAsync(lifecycle, preview, "leaf", TimeSpan.FromDays(30), terminal);
        var before = await db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(m => m.Id == early.CopyId, Ct);
        if (cycle)
        {
            // Privileged malformed graph robustness: the FK permits cycles, while typed creation is a DAG.
            db.EvidenceCopyDependencies.Add(new() { CopyId = early.CopyId, SourceCopyId = terminal.CopyId, SourceVersion = terminal.Version });
            await db.SaveChangesAsync(Ct);
        }
        clock.Advance(TimeSpan.FromDays(20));
        if (rawRemoval) await db.Database.ExecuteSqlRawAsync("UPDATE iracing.\"Weeks\" SET \"DemoWeatherSummaryJson\" = NULL", Ct);
        else await lifecycle.EndPurposeAsync(official, clock.GetUtcNow(), Ct);
        foreach (var id in new[] { early.CopyId, bridge.CopyId, terminal.CopyId, leaf.CopyId })
        {
            var marker = await db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(m => m.Id == id, Ct);
            Assert.Equal(before.ExpiresAt, marker.UnavailableAt);
            Assert.Equal(id == early.CopyId ? before.RemovalDueAt : before.ExpiresAt!.Value.AddDays(7), marker.RemovalDueAt);
        }
        await lifecycle.ReconcileAsync(Ct);
        Assert.Empty(await db.ExternalDataCaches.IgnoreQueryFilters().ToListAsync(Ct));
    }

    [Fact]
    public async Task ApplicationClosureAfterRawRemovalKeepsPersistedEarlierClocksInSameContext()
    {
        await using var db = await CreateAsync();
        var clock = new CopyClock(TimeProvider.System.GetUtcNow().AddDays(-20));
        var week = await CatalogAsync(db);
        var lifecycle = new EvidenceCopyLifecycle(db, clock);
        var official = await lifecycle.OpenSyntheticPurposeAsync(EvidencePurposeKind.IndependentOfficial, 1, ct: Ct);
        var weather = await lifecycle.CaptureAsync(official, EvidenceCopyKind.Weather, "weather", ct: Ct);
        var source = await lifecycle.CommitAsync(weather, new WeatherBatch(week, "{}"), Ct);
        var preview = await lifecycle.OpenSyntheticPurposeAsync(ct: Ct);
        var leaf = await CommitMappedAsync(lifecycle, preview, "leaf", TimeSpan.FromDays(30), source);
        clock.Advance(TimeSpan.FromDays(20));
        await db.Database.ExecuteSqlRawAsync("UPDATE iracing.\"Weeks\" SET \"DemoWeatherSummaryJson\" = NULL", Ct);
        var before = await db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(m => m.Id == leaf.CopyId, Ct);
        clock.Advance(TimeSpan.FromHours(1)); // Later than actual SQL loss; the tracked instance remains stale.
        await lifecycle.EndPurposeAsync(preview, clock.GetUtcNow(), Ct);
        var after = await db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(m => m.Id == leaf.CopyId, Ct);
        Assert.Equal(before.UnavailableAt, after.UnavailableAt);
        Assert.Equal(before.RemovalDueAt, after.RemovalDueAt);
        await lifecycle.ReconcileAsync(Ct);
        Assert.Empty(await db.ExternalDataCaches.IgnoreQueryFilters().ToListAsync(Ct));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3600)]
    [InlineData(176400)]
    public async Task ReconciliationHonorsOriginalExpiryAndDeadlineWithoutWaitingForMaximumRetention(int secondsAfterExpiry)
    {
        await using var db = await CreateAsync();
        var clock = new CopyClock(TimeProvider.System.GetUtcNow().AddDays(-10));
        var lifecycle = new EvidenceCopyLifecycle(db, clock);
        var purpose = await lifecycle.OpenSyntheticPurposeAsync(ct: Ct);
        var receipt = await lifecycle.CaptureAsync(purpose, EvidenceCopyKind.MappedCache, "reconcile", ct: Ct);
        var source = await lifecycle.CommitAsync(receipt, Cache(receipt, TimeSpan.FromDays(1)), Ct);
        var before = await db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(m => m.Id == source.CopyId, Ct);
        clock.Advance(TimeSpan.FromDays(1) + TimeSpan.FromSeconds(secondsAfterExpiry));
        var pending = await lifecycle.InspectAsync(Ct);
        Assert.Equal(secondsAfterExpiry >= 48 * 3600 ? 1 : 0, pending.Overdue);
        await lifecycle.ReconcileAsync(Ct);
        await lifecycle.ReconcileAsync(Ct);
        var after = await db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(m => m.Id == source.CopyId, Ct);
        Assert.Equal(before.OriginalAcquiredAt, after.OriginalAcquiredAt);
        Assert.Equal(before.ExpiresAt, after.ExpiresAt);
        Assert.Equal(before.RemovalDueAt, after.RemovalDueAt);
        Assert.Equal(secondsAfterExpiry < 0 ? null : before.ExpiresAt, after.UnavailableAt);
        Assert.Equal(secondsAfterExpiry < 0 ? 1 : 0, await db.ExternalDataCaches.IgnoreQueryFilters().CountAsync(Ct));
        Assert.Equal(secondsAfterExpiry >= 0, after.VerifiedRemovedAt is not null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedPhysicalErasureRollsBackClocksAndRetryPreservesOriginalDeadline(bool afterMappedDeletion)
    {
        var failure = new FailedErasure(afterMappedDeletion);
        await using var db = await CreateAsync(failure);
        var clock = new CopyClock(TimeProvider.System.GetUtcNow().AddDays(-20));
        var lifecycle = new EvidenceCopyLifecycle(db, clock);
        var purpose = await lifecycle.OpenSyntheticPurposeAsync(ct: Ct);
        var receipt = await lifecycle.CaptureAsync(purpose, EvidenceCopyKind.MappedCache, "rollback", ct: Ct);
        var source = await lifecycle.CommitAsync(receipt, Cache(receipt, TimeSpan.FromHours(1)), Ct);
        var stale = await lifecycle.CaptureAsync(purpose, EvidenceCopyKind.MappedCache, "rollback", ct: Ct);
        var before = await db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(m => m.Id == source.CopyId, Ct);
        clock.Advance(TimeSpan.FromDays(20));
        failure.Armed = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => lifecycle.ReconcileAsync(Ct));
        var rolledBack = await db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(m => m.Id == source.CopyId, Ct);
        Assert.Null(rolledBack.UnavailableAt);
        Assert.Null(rolledBack.VerifiedRemovedAt);
        Assert.Equal(before.RemovalDueAt, rolledBack.RemovalDueAt);
        Assert.Single(await db.ExternalDataCaches.IgnoreQueryFilters().ToListAsync(Ct));
        await lifecycle.ReconcileAsync(Ct);
        await lifecycle.ReconcileAsync(Ct);
        var after = await db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(m => m.Id == source.CopyId, Ct);
        Assert.Equal(before.OriginalAcquiredAt, after.OriginalAcquiredAt);
        Assert.Equal(before.ExpiresAt, after.ExpiresAt);
        Assert.Equal(before.ExpiresAt, after.UnavailableAt);
        Assert.Equal(before.RemovalDueAt, after.RemovalDueAt);
        Assert.NotNull(after.VerifiedRemovedAt);
        Assert.Empty(await db.ExternalDataCaches.IgnoreQueryFilters().ToListAsync(Ct));
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => lifecycle.CommitAsync(stale, Cache(stale, TimeSpan.FromDays(30)), Ct));
    }

    [Fact]
    public async Task FailedReplacementAfterPhysicalDeleteRollsBackPayloadLossAndRemovalReceipt()
    {
        var failure = new FailedReplacement();
        await using var db = await CreateAsync(failure);
        var clock = new CopyClock(TimeProvider.System.GetUtcNow().AddHours(-1));
        var lifecycle = new EvidenceCopyLifecycle(db, clock);
        var purpose = await lifecycle.OpenSyntheticPurposeAsync(ct: Ct);
        var receipt = await lifecycle.CaptureAsync(purpose, EvidenceCopyKind.MappedCache, "replacement-rollback", ct: Ct);
        var original = await lifecycle.CommitAsync(receipt, Cache(receipt, TimeSpan.FromDays(1)), Ct);
        var before = await db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(m => m.Id == original.CopyId, Ct);
        clock.Advance(TimeSpan.FromMinutes(1));
        var replacement = await lifecycle.CaptureAsync(purpose, EvidenceCopyKind.MappedCache, "replacement-rollback", ct: Ct);
        failure.Armed = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => lifecycle.CommitAsync(replacement,
            Cache(replacement, TimeSpan.FromDays(1)), Ct));

        Assert.True(failure.Reached);
        Assert.Empty(db.ChangeTracker.Entries());
        await db.SaveChangesAsync(Ct); // No abandoned retirement can leak into a subsequent save.
        var rolledBack = await db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(m => m.Id == original.CopyId, Ct);
        Assert.Equal(before.UnavailableAt, rolledBack.UnavailableAt);
        Assert.Equal(before.RemovalDueAt, rolledBack.RemovalDueAt);
        Assert.Null(rolledBack.VerifiedRemovedAt);
        Assert.Equal(original.CopyId, Assert.Single(await db.ExternalDataCaches.IgnoreQueryFilters().ToListAsync(Ct)).EvidenceCopyId);

        var next = await lifecycle.CommitAsync(replacement, Cache(replacement, TimeSpan.FromDays(1)), Ct);
        var retired = await db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(m => m.Id == original.CopyId, Ct);
        Assert.Equal(before.OriginalAcquiredAt, retired.OriginalAcquiredAt);
        Assert.Equal(before.ExpiresAt, retired.ExpiresAt);
        Assert.Equal(clock.GetUtcNow(), retired.UnavailableAt);
        Assert.Equal(clock.GetUtcNow(), retired.RemovalDueAt);
        Assert.Equal(clock.GetUtcNow(), retired.VerifiedRemovedAt);
        Assert.Equal(next.CopyId, Assert.Single(await db.ExternalDataCaches.IgnoreQueryFilters().ToListAsync(Ct)).EvidenceCopyId);
    }

    private sealed class FailedReplacement : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public bool Reached { get; private set; }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            // Mapped payload deletion precedes this statement in physical removal.
            if (Armed && command.CommandText.Contains("DELETE") && command.CommandText.Contains("RaceEvidenceResults"))
            {
                Armed = false;
                Reached = true;
                throw new InvalidOperationException("Controlled replacement failure after mapped deletion.");
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FailedErasure(bool afterMappedDeletion) : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Armed && command.CommandText.Contains("DELETE") && command.CommandText.Contains(afterMappedDeletion ? "RaceEvidenceResults" : "MappedDataCaches"))
            {
                Armed = false;
                throw new InvalidOperationException("Controlled physical-erasure failure.");
            }
            return ValueTask.FromResult(result);
        }
    }

    private static async Task<EvidenceSourceVersion> CommitMappedAsync(EvidenceCopyLifecycle lifecycle, Guid purpose,
        string key, TimeSpan ttl, params EvidenceSourceVersion[] sources)
    {
        var receipt = await lifecycle.CaptureAsync(purpose, EvidenceCopyKind.MappedCache, key, sources, Ct);
        return await lifecycle.CommitAsync(receipt, Cache(receipt, ttl), Ct);
    }

    private async Task<AppDbContext> CreateAsync(params IInterceptor[] interceptors)
    {
        var options = await postgres.CreateOptionsAsync(Ct);
        var db = new AppDbContext(postgres.WithInterceptors(options, interceptors), new IRacingDataScope(DataProvenance.Demo));
        await db.Database.EnsureDeletedAsync(Ct);
        await db.Database.MigrateAsync(Ct);
        return db;
    }

    private static async Task<Guid> CatalogAsync(AppDbContext db)
    {
        db.Series.Add(new() { Id = 1, Name = "Synthetic Series" });
        db.Seasons.Add(new() { Id = 1, SeriesId = 1, Year = 2026, Quarter = 1, Active = true });
        db.Tracks.Add(new() { Id = 1, Name = "Synthetic Track" });
        var week = Guid.NewGuid();
        db.Weeks.Add(new() { Id = week, SeasonId = 1, TrackId = 1 });
        await db.SaveChangesAsync(Ct);
        return week;
    }

    private static MappedCacheBatch Cache(EvidenceWriteReceipt receipt, TimeSpan ttl) => new(new ExternalDataCache
    {
        CacheKey = receipt.KeyHash,
        Payload = "{}",
        FetchedAt = receipt.OriginalAcquiredAt,
        ExpiresAt = receipt.OriginalAcquiredAt.Add(ttl)
    });

    private sealed class CopyClock(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset _now = DriverAuthorizationPolicy.DurableTime(initial);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
