using System.Data.Common;
using System.Diagnostics;
using System.Collections;
using System.Text.Json;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Core.PercentileHistory;
using ApexRacers.Data;
using ApexRacers.Api.Services;
using ApexRacers.Seeder;
using ApexRacers.Seeder.Demo;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using Npgsql;

namespace ApexRacers.Tests.Services;

[Collection(PostgreSqlCollection.Name)]
public sealed class PercentileRefreshTests(PostgreSqlFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly PercentileSelection RaceOnly = PercentileSelection.FromRequest(false, null);

    [Fact]
    public async Task SuccessiveFiveFourFiveMissingBestAndSourceLossReplaceOrRemoveOnlyCurrentSelection()
    {
        await using var f = await CreateAsync(1, 1);
        var first = await f.Refresh.RefreshDemoAsync(f.User, RaceOnly, 1, f.Weeks, [1], Ct);
        Assert.Equal(50, Assert.Single(first.Readings).PercentileRank);
        Assert.Single(await f.Db.ClassifiedCarPercentileResults.ToListAsync(Ct));
        await f.Refresh.RefreshDemoAsync(f.User, RaceOnly, 1, f.Weeks, [1], Ct);
        Assert.Single(await f.Db.ClassifiedCarPercentileResults.ToListAsync(Ct));
        var unrestricted = PercentileSelection.FromRequest(true, null);
        await f.Refresh.RefreshDemoAsync(f.User, unrestricted, 1, f.Weeks, [1], Ct);
        Assert.Equal(2, await f.Db.ClassifiedCarPercentileResults.CountAsync(Ct));
        await f.WriteAsync(0, 1, [110, 100, 105, 115]);
        var small = await f.Refresh.RefreshDemoAsync(f.User, RaceOnly, 1, f.Weeks, [1], Ct);
        Assert.Equal(4, Assert.Single(small.Readings).FieldSize);
        Assert.False(small.Readings[0].IsPresentable);
        Assert.Empty(await f.Db.ClassifiedCarPercentileResults.Where(r => r.SelectionKey == RaceOnly.Key).ToListAsync(Ct));
        await f.WriteAsync(0, 1, [110, 100, 105, 115, 120]);
        var grown = await f.Refresh.RefreshDemoAsync(f.User, RaceOnly, 1, f.Weeks, [1], Ct);
        Assert.True(Assert.Single(grown.Readings).IsPresentable);
        Assert.Single(await f.Db.ClassifiedCarPercentileResults.Where(r => r.SelectionKey == RaceOnly.Key).ToListAsync(Ct));
        await f.WriteAsync(0, 1, [100, 105, 115, 120, 125], subject: false);
        var missing = await f.Refresh.RefreshDemoAsync(f.User, RaceOnly, 1, f.Weeks, [1], Ct);
        Assert.Null(Assert.Single(missing.Readings).PersonalBest);
        Assert.Null(missing.Readings[0].FieldSize);
        Assert.Empty(await f.Db.ClassifiedCarPercentileResults.Where(r => r.SelectionKey == RaceOnly.Key).ToListAsync(Ct));
        await f.WriteAsync(0, 1, [110, 100, 105, 115, 120]);
        await f.Refresh.RefreshDemoAsync(f.User, RaceOnly, 1, f.Weeks, [1], Ct);
        await using (var writer = f.Open())
            await new EvidenceCopyLifecycle(writer, TimeProvider.System).EndPurposeAsync(f.FieldPurpose, DateTimeOffset.UtcNow, Ct);
        var lost = await f.Refresh.RefreshDemoAsync(f.User, RaceOnly, 1, f.Weeks, [1], Ct);
        Assert.Null(Assert.Single(lost.Readings).PersonalBest);
        Assert.Empty(await f.Db.ClassifiedCarPercentileResults.IgnoreQueryFilters().Where(r => r.SelectionKey == RaceOnly.Key).ToListAsync(Ct));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BatchReplacementRetiresTrackedAndUntrackedReadingsWithoutResurrectingOldRows(bool trackOldRows)
    {
        await using var f = await CreateAsync(2, 2);
        await f.Refresh.RefreshDemoAsync(f.User, RaceOnly, 1, f.Weeks, [1, 2], Ct);
        var oldRows = await f.Db.ClassifiedCarPercentileResults.AsNoTracking().ToListAsync(Ct);
        var oldIds = oldRows.Select(r => r.EvidenceCopyId!.Value).ToArray();
        var original = await f.Db.EvidenceCopyMarkers.AsNoTracking().Where(m => oldIds.Contains(m.Id)).ToListAsync(Ct);
        await using var reader = f.Open();
        if (trackOldRows)
        {
            await reader.ClassifiedCarPercentileResults.ToListAsync(Ct);
            await reader.EvidenceCopyMarkers.Where(m => oldIds.Contains(m.Id)).ToListAsync(Ct);
        }
        var refresh = new PercentileRefresh(reader, TimeProvider.System, new UnavailableDriverEnforcementJournal());

        var result = await refresh.RefreshDemoAsync(f.User, RaceOnly, 1, f.Weeks, [1, 2], Ct);

        Assert.Equal(4, result.ChangedRows);
        // An unrelated later save must not retry updates to physically retired classified rows.
        var user = await reader.Users.SingleAsync(u => u.Id == f.User, Ct);
        user.DisplayName = "Updated synthetic User";
        await reader.SaveChangesAsync(Ct);
        Assert.DoesNotContain(reader.ChangeTracker.Entries<ClassifiedCarPercentileResult>(),
            e => oldIds.Contains(e.Entity.EvidenceCopyId ?? Guid.Empty));
        var current = await reader.ClassifiedCarPercentileResults.IgnoreQueryFilters().AsNoTracking().ToListAsync(Ct);
        Assert.Equal(4, current.Count);
        Assert.DoesNotContain(current, r => oldIds.Contains(r.EvidenceCopyId!.Value));
        foreach (var before in original)
        {
            var retired = await reader.EvidenceCopyMarkers.AsNoTracking().SingleAsync(m => m.Id == before.Id, Ct);
            Assert.Equal(before.OriginalAcquiredAt, retired.OriginalAcquiredAt);
            Assert.NotNull(retired.UnavailableAt);
            Assert.NotNull(retired.RemovalDueAt);
            Assert.NotNull(retired.VerifiedRemovedAt);
        }
    }

    [Fact]
    public async Task MultiCarMultiWeekBatchHasConstantSourceAndSelectQueriesAndOneOriginalPreparationStamp()
    {
        await using var f = await CreateAsync(2, 3);
        await f.Refresh.RefreshDemoAsync(f.User, RaceOnly, 1, f.Weeks, [1, 2, 3], Ct);
        var probe = new Commands();
        await using var measured = f.Open(probe);
        var refresh = new PercentileRefresh(measured, TimeProvider.System, new UnavailableDriverEnforcementJournal());
        probe.Reset();
        var small = await refresh.RefreshDemoAsync(f.User, RaceOnly, 1, [f.Weeks[0]], [1], Ct);
        var smallCommands = probe.Total; var smallSelects = probe.Selects;
        probe.Reset();
        var big = await refresh.RefreshDemoAsync(f.User, RaceOnly, 1, f.Weeks, [1, 2, 3], Ct);
        var bigCommands = probe.Total; var bigSelects = probe.Selects;
        Assert.Equal(2, small.Measurements.SourceQueries);
        Assert.Equal(2, big.Measurements.SourceQueries);
        Assert.Equal(10, small.Measurements.MaterializedSourceRows);
        Assert.Equal(60, big.Measurements.MaterializedSourceRows);
        Assert.Equal(6, big.Readings.Count);
        Assert.Equal(6, big.ChangedRows);
        Assert.True(bigSelects <= smallSelects + 2, $"Per-cell SELECT growth: {smallSelects} -> {bigSelects}");
        var persisted = await f.Db.ClassifiedCarPercentileResults.AsNoTracking().ToListAsync(Ct);
        Assert.Equal(6, persisted.Count);
        Assert.Single(persisted.Select(r => (r.PreparationPurposeId, r.PreparationGeneration, r.PreparationVersion)).Distinct());
        Artifact("batch-measurements", new { small = new { smallCommands, smallSelects, small.Measurements },
            batch = new { bigCommands, bigSelects, big.Measurements }, cells = 6 });
    }

    [Fact]
    public async Task NewFieldInPreviouslyEmptyTargetAfterReadRejectsEntireBatchRatherThanCommittingOtherCells()
    {
        await using var f = await CreateAsync(1, 2);
        await using (var writer = f.Open())
        {
            var marker = await writer.SubsessionResults.Where(r => r.CarId == 2).Select(r => r.EvidenceCopyId!.Value).FirstAsync(Ct);
            await writer.SubsessionResults.Where(r => r.CarId == 2).ExecuteDeleteAsync(Ct);
            // A deleted field is unavailable. The old marker remains history, never an empty input authority.
            Assert.NotNull(await writer.EvidenceCopyMarkers.SingleAsync(m => m.Id == marker, Ct));
        }
        var gate = new AfterSourceRead(async () => await f.WriteAsync(0, 2, [110, 100, 105, 115, 120]));
        await using var reader = f.Open(gate);
        var refresh = new PercentileRefresh(reader, TimeProvider.System, new UnavailableDriverEnforcementJournal());
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => refresh.RefreshDemoAsync(f.User, RaceOnly, 1, f.Weeks, [1, 2], Ct));
        Assert.True(gate.Reached);
        Assert.Empty(await f.Db.ClassifiedCarPercentileResults.IgnoreQueryFilters().ToListAsync(Ct));
        var fresh = await f.Refresh.RefreshDemoAsync(f.User, RaceOnly, 1, f.Weeks, [1, 2], Ct);
        Assert.Equal(2, fresh.Readings.Count(r => r.IsPresentable));
    }

    [Fact]
    public async Task ExistingCatalogMutationLockDeniesRefreshWithoutWaitingOrReturningFreshOutput()
    {
        await using var f = await CreateAsync(1, 1);
        await using var holder = f.Open();
        await using var transaction = await holder.Database.BeginTransactionAsync(Ct);
        await holder.Database.ExecuteSqlRawAsync("LOCK TABLE iracing.\"Weeks\" IN ROW EXCLUSIVE MODE", Ct);
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => f.Refresh.RefreshDemoAsync(f.User, RaceOnly, 1, f.Weeks, [1], Ct));
        Assert.Empty(await f.Db.ClassifiedCarPercentileResults.IgnoreQueryFilters().ToListAsync(Ct));
        await transaction.RollbackAsync(Ct);
        Assert.True(Assert.Single((await f.Refresh.RefreshDemoAsync(f.User, RaceOnly, 1, f.Weeks, [1], Ct)).Readings).IsPresentable);
    }

    [Fact]
    public async Task CatalogMutationAfterFinalWitnessBlocksThroughCommitThenFreshRefreshUsesNewLayout()
    {
        await using var f = await CreateAsync(1, 1);
        f.Db.Tracks.Add(new() { Id = 2, Name = "Synthetic Track", ConfigName = "Other layout" });
        await f.Db.SaveChangesAsync(Ct);
        Task? mutation = null;
        var blocked = false;
        var gate = new AfterCalendarRead(async () =>
        {
            mutation = Task.Run(async () =>
            {
                await using var writer = f.Open();
                await writer.Weeks.Where(w => w.Id == f.Weeks[0]).ExecuteUpdateAsync(u => u.SetProperty(w => w.TrackId, 2), Ct);
            }, Ct);
            await using var observer = f.Open();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            while (!blocked)
            {
                blocked = await observer.Database.SqlQueryRaw<bool>("SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE datname = current_database() AND query LIKE 'UPDATE iracing.%Weeks%TrackId%' AND wait_event_type = 'Lock') AS \"Value\"").SingleAsync(timeout.Token);
                if (!blocked) await Task.Delay(10, timeout.Token);
            }
        });
        await using var reader = f.Open(gate);
        var output = await new PercentileRefresh(reader, TimeProvider.System, new UnavailableDriverEnforcementJournal())
            .RefreshDemoAsync(f.User, RaceOnly, 1, f.Weeks, [1], Ct);
        Assert.True(blocked);
        Assert.True(Assert.Single(output.Readings).IsPresentable);
        Assert.NotNull(mutation);
        await mutation;
        var refreshed = await f.Refresh.RefreshDemoAsync(f.User, RaceOnly, 1, f.Weeks, [1], Ct);
        Assert.Null(Assert.Single(refreshed.Readings).PersonalBest);
        Assert.Empty(await f.Db.ClassifiedCarPercentileResults.IgnoreQueryFilters().ToListAsync(Ct));
    }

    [Fact]
    public async Task DiscoveredHistoryWitnessesAllSeriesWeeksLabelsAndOwnCommitVersion()
    {
        await using var f = await CreateAsync(2, 2);
        var snapshot = await f.Refresh.ReadDemoHistoryAsync(f.User, RaceOnly, ct: Ct);
        Assert.Equal(4, snapshot.Readings.Count);
        Assert.Equal(2, snapshot.Weeks.Count);
        Assert.Equal(2, snapshot.CarNames.Count);
        Assert.Equal(DemoData.DriverCustId, snapshot.CustomerId);
        Assert.True(await f.Refresh.SnapshotCurrentAsync(snapshot, Ct));
        Assert.All(snapshot.Readings, r => Assert.Equal(50, r.PercentileRank));
        await using (var writer = f.Open())
            await writer.Tracks.Where(t => t.Id == 1).ExecuteUpdateAsync(u => u.SetProperty(t => t.Name, "Corrected venue"), Ct);
        Assert.False(await f.Refresh.SnapshotCurrentAsync(snapshot, Ct));
        var fresh = await f.Refresh.ReadDemoHistoryAsync(f.User, RaceOnly, ct: Ct);
        Assert.All(fresh.Weeks, w => Assert.Equal("Corrected venue", w.TrackName));
        Assert.True(await f.Refresh.SnapshotCurrentAsync(fresh, Ct));
    }

    [Fact]
    public async Task UnchangedHistoryPollingKeepsReadingIdentityPurposeVersionAndOriginalClocks()
    {
        await using var f = await CreateAsync(1, 1);
        var first = await f.Refresh.ReadDemoHistoryAsync(f.User, RaceOnly, ct: Ct);
        var copy = Assert.Single(await f.Db.ClassifiedCarPercentileResults.AsNoTracking().ToListAsync(Ct));
        var marker = await f.Db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(m => m.Id == copy.EvidenceCopyId, Ct);
        var version = await f.Db.EvidencePurposes.Where(p => p.Id == copy.PreparationPurposeId).Select(p => p.EvidenceVersion).SingleAsync(Ct);
        var second = await f.Refresh.ReadDemoHistoryAsync(f.User, RaceOnly, ct: Ct);
        var retained = Assert.Single(await f.Db.ClassifiedCarPercentileResults.AsNoTracking().ToListAsync(Ct));
        Assert.Equal(copy.Id, retained.Id);
        Assert.Equal(copy.EvidenceCopyId, retained.EvidenceCopyId);
        Assert.Equal(copy.ComputedAt, retained.ComputedAt);
        Assert.Equal(marker.OriginalAcquiredAt, (await f.Db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(m => m.Id == retained.EvidenceCopyId, Ct)).OriginalAcquiredAt);
        Assert.Equal(version, await f.Db.EvidencePurposes.Where(p => p.Id == copy.PreparationPurposeId).Select(p => p.EvidenceVersion).SingleAsync(Ct));
        Assert.True(await f.Refresh.SnapshotCurrentAsync(first, Ct));
        Assert.True(await f.Refresh.SnapshotCurrentAsync(second, Ct));
        var otherLaps = Assert.IsAssignableFrom<IList<double>>(Assert.Single(second.Readings).OtherDriversLaps);
        Assert.Throws<NotSupportedException>(() => otherLaps[0] = 1);
    }

    [Fact]
    public async Task EmptyHistoryWitnessIncludesEmptySeasonsAndDetectsNewCatalogWeek()
    {
        await using var f = await CreateAsync(0, 1);
        var empty = await f.Refresh.ReadDemoHistoryAsync(f.User, RaceOnly, ct: Ct);
        Assert.Empty(empty.Readings);
        Assert.Empty(empty.Weeks);
        Assert.Single(empty.Seasons);
        Assert.True(await f.Refresh.SnapshotCurrentAsync(empty, Ct));
        await using (var writer = f.Open())
        {
            writer.Weeks.Add(new() { Id = Guid.NewGuid(), SeasonId = 1, TrackId = 1, StartDate = new(2026, 10, 1) });
            await writer.SaveChangesAsync(Ct);
        }
        Assert.False(await f.Refresh.SnapshotCurrentAsync(empty, Ct));
        var current = await f.Refresh.ReadDemoHistoryAsync(f.User, RaceOnly, ct: Ct);
        Assert.Single(current.Weeks);
        Assert.Empty(current.Readings);
        Assert.True(await f.Refresh.SnapshotCurrentAsync(current, Ct));
    }

    [Fact]
    public async Task NewCarAndNewWeekAfterDiscoveryRejectSnapshotRatherThanHidingOutsideRequestedMatrix()
    {
        await using var f = await CreateAsync(1, 1);
        var initial = await f.Refresh.ReadDemoHistoryAsync(f.User, RaceOnly, ct: Ct);
        await using (var writer = f.Open())
        {
            writer.Cars.Add(new() { Id = 2, Name = "New Field Car", NameAbbreviated = "NC" });
            await writer.SaveChangesAsync(Ct);
        }
        await f.WriteAsync(0, 2, [110, 100, 105, 115, 120]);
        Assert.False(await f.Refresh.SnapshotCurrentAsync(initial, Ct));
        var withCar = await f.Refresh.ReadDemoHistoryAsync(f.User, RaceOnly, ct: Ct);
        Assert.Equal(2, withCar.Readings.Count);
        var gate = new AfterSourceRead(async () =>
        {
            await using var writer = f.Open();
            writer.Weeks.Add(new() { Id = Guid.NewGuid(), SeasonId = 1, RaceWeekIndex = 1, TrackId = 1, StartDate = new(2026, 10, 8) });
            await writer.SaveChangesAsync(Ct);
        });
        await using var reader = f.Open(gate);
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() =>
            new PercentileRefresh(reader, TimeProvider.System, new UnavailableDriverEnforcementJournal()).ReadDemoHistoryAsync(f.User, RaceOnly, ct: Ct));
        Assert.True(gate.Reached);
    }

    [Fact]
    public async Task NewCarSourceDuringDiscoveryCannotHideOutsideInitialCarUniverse()
    {
        await using var f = await CreateAsync(1, 1);
        var gate = new AfterSourceRead(async () =>
        {
            await using (var writer = f.Open())
            {
                writer.Cars.Add(new() { Id = 2, Name = "Arriving Car", NameAbbreviated = "AC" });
                await writer.SaveChangesAsync(Ct);
            }
            await f.WriteAsync(0, 2, [110, 100, 105, 115, 120]);
        });
        await using var reader = f.Open(gate);
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() =>
            new PercentileRefresh(reader, TimeProvider.System, new UnavailableDriverEnforcementJournal()).ReadDemoHistoryAsync(f.User, RaceOnly, ct: Ct));
        Assert.True(gate.Reached);
        Assert.Empty(await f.Db.ClassifiedCarPercentileResults.IgnoreQueryFilters().ToListAsync(Ct));
        Assert.Equal(2, (await f.Refresh.ReadDemoHistoryAsync(f.User, RaceOnly, ct: Ct)).Readings.Count);
    }

    [Fact]
    public async Task SourceLossRemovesOldOnlyDiscoveredTargetsAndOwnRemovalDoesNotInvalidateFreshSnapshot()
    {
        await using var f = await CreateAsync(1, 1);
        var initial = await f.Refresh.ReadDemoHistoryAsync(f.User, RaceOnly, ct: Ct);
        await using (var writer = f.Open())
            await new EvidenceCopyLifecycle(writer, TimeProvider.System).EndPurposeAsync(f.FieldPurpose, DateTimeOffset.UtcNow, Ct);
        Assert.False(await f.Refresh.SnapshotCurrentAsync(initial, Ct));
        var lost = await f.Refresh.ReadDemoHistoryAsync(f.User, RaceOnly, ct: Ct);
        Assert.Null(Assert.Single(lost.Readings).PersonalBest);
        Assert.Empty(await f.Db.ClassifiedCarPercentileResults.IgnoreQueryFilters().ToListAsync(Ct));
        Assert.True(await f.Refresh.SnapshotCurrentAsync(lost, Ct));
        var empty = await f.Refresh.ReadDemoHistoryAsync(f.User, RaceOnly, ct: Ct);
        Assert.Empty(empty.Readings);
        Assert.True(await f.Refresh.SnapshotCurrentAsync(empty, Ct));
    }

    [Fact]
    public async Task DiscoveredHistoryReadsAndFinalWitnessStayBatchedWithMeasuredSourceRows()
    {
        async Task<object> MeasureAsync(int weeks, int cars)
        {
            await using var f = await CreateAsync(weeks, cars);
            await f.Refresh.ReadDemoHistoryAsync(f.User, RaceOnly, ct: Ct); // Warm idempotent retained derivatives.
            var commands = new HistoryCommands();
            await using var db = f.Open(commands);
            var refresh = new PercentileRefresh(db, TimeProvider.System, new UnavailableDriverEnforcementJournal());
            var process = Process.GetCurrentProcess();
            var cpu = process.TotalProcessorTime;
            var wall = Stopwatch.StartNew();
            var snapshot = await refresh.ReadDemoHistoryAsync(f.User, RaceOnly, ct: Ct);
            var read = new { commands.Total, commands.Selects, commands.SourceQueries, commands.SourceRows,
                commands.SelectedRows, ProcessCpuMilliseconds = (process.TotalProcessorTime - cpu).TotalMilliseconds,
                WallMilliseconds = wall.Elapsed.TotalMilliseconds };
            Assert.Equal(weeks * cars, snapshot.Readings.Count);
            Assert.Equal(2, commands.SourceQueries);
            Assert.Equal(weeks * cars * 10, commands.SourceRows);
            commands.Reset();
            wall.Restart(); cpu = process.TotalProcessorTime;
            Assert.True(await refresh.SnapshotCurrentAsync(snapshot, Ct));
            var finalWitness = new { commands.Total, commands.Selects, commands.SourceQueries, commands.SourceRows,
                commands.SelectedRows, ProcessCpuMilliseconds = (process.TotalProcessorTime - cpu).TotalMilliseconds,
                WallMilliseconds = wall.Elapsed.TotalMilliseconds };
            Assert.Equal(1, commands.SourceQueries);
            Assert.Equal(weeks * cars * 5, commands.SourceRows);
            return new { Cells = weeks * cars, Read = read, FinalWitness = finalWitness };
        }
        var small = JsonSerializer.SerializeToElement(await MeasureAsync(1, 1));
        var batch = JsonSerializer.SerializeToElement(await MeasureAsync(2, 3));
        Assert.True(batch.GetProperty("Read").GetProperty("Selects").GetInt32()
            <= small.GetProperty("Read").GetProperty("Selects").GetInt32() + 2);
        Assert.True(batch.GetProperty("FinalWitness").GetProperty("Selects").GetInt32()
            <= small.GetProperty("FinalWitness").GetProperty("Selects").GetInt32() + 2);
        Artifact("discovered-history-measurements", new { small, batch });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OverlappingColdUserDemoReadsDoNotInvalidateEachOtherWhenNoSourceChanged(bool differentUser)
    {
        await using var f = await CreateAsync(1, 1);
        await f.Refresh.ReadDemoHistoryAsync(f.User, RaceOnly, ct: Ct);
        var newUser = Guid.NewGuid();
        f.Db.Users.Add(new() { Id = newUser, DisplayName = "Claimless post-seed User" });
        var secondUser = differentUser ? Guid.NewGuid() : newUser;
        if (differentUser) f.Db.Users.Add(new() { Id = secondUser, DisplayName = "Another claimless User" });
        await f.Db.SaveChangesAsync(Ct);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new AfterSourceRead(async () => { reached.TrySetResult(); await release.Task.WaitAsync(Ct); });
        // Both readers must progress with only two shared physical connections. An auxiliary
        // pooled gate connection per waiter would starve the active reader's source connection.
        var pool = new NpgsqlConnectionStringBuilder(f.Db.Database.GetConnectionString()) { MaxPoolSize = 2, Pooling = true };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(pool.ConnectionString).Options;
        await using var firstDb = new AppDbContext(postgres.WithInterceptors(options, gate), new IRacingDataScope(DataProvenance.Demo));
        await using var secondDb = new AppDbContext(options, new IRacingDataScope(DataProvenance.Demo));
        var first = new PercentileRefresh(firstDb, TimeProvider.System, new UnavailableDriverEnforcementJournal())
            .ReadDemoHistoryAsync(newUser, RaceOnly, ct: Ct);
        await reached.Task.WaitAsync(Ct);
        var second = new PercentileRefresh(secondDb, TimeProvider.System, new UnavailableDriverEnforcementJournal())
            .ReadDemoHistoryAsync(secondUser, RaceOnly, ct: Ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await using var observer = f.Open();
        try
        {
            // Wait for the second actual request either to finish or to wait at history-read
            // admission. A retained-source writer is never held behind this test barrier.
            while (!second.IsCompleted)
            {
                if (await observer.Database.SqlQueryRaw<bool>("SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock' AND wait_event = 'advisory') AS \"Value\"").SingleAsync(timeout.Token)) break;
                await Task.Delay(10, timeout.Token);
            }
        }
        finally { release.TrySetResult(); }
        var histories = await Task.WhenAll(first, second);
        Assert.All(histories, history => Assert.True(Assert.Single(history.Readings).IsPresentable));
        Assert.Single(await f.Db.ClassifiedCarPercentileResults.Where(r => r.UserId == newUser).ToListAsync(Ct));
        Assert.Single(await f.Db.ClassifiedCarPercentileResults.Where(r => r.UserId == secondUser).ToListAsync(Ct));
    }

    [Fact]
    public async Task CancellationWhileWaitingForDemoGateReleasesWaiterWithoutUnlockingCurrentHolder()
    {
        await using var f = await CreateAsync(1, 1);
        await using var holder = f.Open();
        await holder.Database.OpenConnectionAsync(Ct);
        await holder.Database.ExecuteSqlRawAsync("SELECT pg_advisory_lock(428)", Ct);
        try
        {
            using var request = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            await using var waitingDb = f.Open();
            var waiting = new PercentileRefresh(waitingDb, TimeProvider.System, new UnavailableDriverEnforcementJournal())
                .ReadDemoHistoryAsync(f.User, RaceOnly, ct: request.Token);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            await using var probe = f.Open();
            while (!await probe.Database.SqlQueryRaw<bool>("SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock' AND wait_event = 'advisory') AS \"Value\"").SingleAsync(deadline.Token))
                await Task.Delay(10, deadline.Token);
            request.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(deadline.Token));
            Assert.False(await probe.Database.SqlQueryRaw<bool>("SELECT pg_try_advisory_lock(428) AS \"Value\"").SingleAsync(Ct));
            Assert.Empty(await f.Db.ClassifiedCarPercentileResults.ToListAsync(Ct));
        }
        finally
        {
            await holder.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock(428)", Ct);
            await holder.Database.CloseConnectionAsync();
        }
        Assert.True(Assert.Single((await f.Refresh.ReadDemoHistoryAsync(f.User, RaceOnly, ct: Ct)).Readings).IsPresentable);
    }

    [Fact]
    public async Task CancellationAfterServerAcquiresGateStillUnlocksCallerOpenedPhysicalSession()
    {
        await using var f = await CreateAsync(1, 1);
        using var request = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var fault = new DemoGateFault(request, failUnlock: false);
        await using var db = f.Open(fault);
        await db.Database.OpenConnectionAsync(Ct); // Caller owns a separate EF open count.
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new PercentileRefresh(db, TimeProvider.System,
                new UnavailableDriverEnforcementJournal()).ReadDemoHistoryAsync(f.User, RaceOnly, ct: request.Token));
            Assert.True(fault.Reached);
            Assert.Equal(System.Data.ConnectionState.Open, db.Database.GetDbConnection().State);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            Assert.True(Assert.Single((await f.Refresh.ReadDemoHistoryAsync(f.User, RaceOnly, ct: deadline.Token)).Readings).IsPresentable);
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    [Fact]
    public async Task UncertainGateUnlockDiscardsEvenCallerOpenedSessionAndDoesNotReturnPreparedSuccess()
    {
        await using var f = await CreateAsync(1, 1);
        var fault = new DemoGateFault(null, failUnlock: true);
        await using var db = f.Open(fault);
        await db.Database.OpenConnectionAsync(Ct);
        var originalPid = await db.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync(Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new PercentileRefresh(db, TimeProvider.System,
            new UnavailableDriverEnforcementJournal()).ReadDemoHistoryAsync(f.User, RaceOnly, ct: Ct));
        Assert.True(fault.Reached);
        Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        while (await f.Db.Database.SqlQuery<bool>($"SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE pid = {originalPid}) AS \"Value\"").SingleAsync(deadline.Token))
            await Task.Delay(10, deadline.Token);
        Assert.True(Assert.Single((await f.Refresh.ReadDemoHistoryAsync(f.User, RaceOnly, ct: deadline.Token)).Readings).IsPresentable);
    }

    [Fact]
    public async Task FreshClaimlessUserAfterActualCiDemoSeedCanReadOrdinaryAnalytics()
    {
        var options = await postgres.CreateOptionsAsync(Ct);
        await using (var setup = new AppDbContext(options))
        { await setup.Database.EnsureDeletedAsync(Ct); await setup.Database.MigrateAsync(Ct); }
        var user = Guid.NewGuid();
        await using (var seed = new AppDbContext(options, new IRacingDataScope(DataProvenance.Demo)))
        {
            await new CiCatalogSeeder(seed).SeedAsync();
            await new DemoPercentileSeeder(seed).SeedAsync(Ct);
            await new DemoCacheSeeder(seed).SeedAllAsync(Ct);
            seed.Users.Add(new() { Id = user, DisplayName = "New claimless Demo User", EmailConfirmed = true });
            var flag = await seed.FeatureFlags.SingleAsync(f => f.Key == "iracing-demo", Ct);
            flag.IsEnabled = true; flag.MinimumRole = "Standard";
            await seed.SaveChangesAsync(Ct);
        }
        await using var db = new AppDbContext(options, new IRacingDataScope(DataProvenance.Demo));
        var analytics = await new UserAnalyticsService(db).GetAnalyticsAsync(user, CiCatalog.SeriesId,
            PersonalBestEvidence.FromRequest(true, null), Ct);
        Assert.Equal(CiCatalog.CarIds.Length, analytics.Count);
        Assert.All(analytics, car =>
        {
            Assert.Equal(DemoData.DriverCustId, car.CustomerId);
            Assert.Equal(CiCatalog.WeekCount, car.TotalWeeks);
            Assert.All(car.PercentileHistory, week => Assert.Equal(LapEvidence.RaceLap, week.PersonalBestLapEvidence));
        });
        Assert.Null((await db.Users.FindAsync([user], Ct))!.IRacingCustomerId);
    }

    private async Task<Fixture> CreateAsync(int weeks, int cars)
    {
        var options = await postgres.CreateOptionsAsync(Ct);
        await using (var setup = new AppDbContext(options))
        { await setup.Database.EnsureDeletedAsync(Ct); await setup.Database.MigrateAsync(Ct); }
        var fixture = new Fixture(postgres, options);
        fixture.Db.Users.Add(new() { Id = fixture.User, DisplayName = "Synthetic User", IRacingCustomerId = 777 });
        fixture.Db.Series.Add(new() { Id = 1, Name = "Synthetic Series" });
        fixture.Db.Seasons.Add(new() { Id = 1, SeriesId = 1, Active = true });
        fixture.Db.CarClasses.Add(new() { Id = 1, Name = "Synthetic Class", ShortName = "SC" });
        fixture.Db.Tracks.Add(new() { Id = 1, Name = "Synthetic Track" });
        for (var car = 1; car <= cars; car++) fixture.Db.Cars.Add(new() { Id = car, Name = "Synthetic Car " + car, NameAbbreviated = "SC" });
        for (var index = 0; index < weeks; index++)
        {
            var id = Guid.NewGuid(); fixture.Weeks.Add(id);
            fixture.Db.Weeks.Add(new() { Id = id, SeasonId = 1, RaceWeekIndex = index, TrackId = 1, StartDate = new DateOnly(2026, 10, 1).AddDays(index * 7) });
        }
        await fixture.Db.SaveChangesAsync(Ct);
        fixture.FieldPurpose = await new EvidenceCopyLifecycle(fixture.Db, TimeProvider.System)
            .OpenSyntheticPurposeAsync(EvidencePurposeKind.IndependentOfficial, 1, ct: Ct);
        for (var week = 0; week < weeks; week++)
        for (var car = 1; car <= cars; car++) await fixture.WriteAsync(week, car, [110, 100, 105, 115, 120]);
        return fixture;
    }

    private sealed class Fixture(PostgreSqlFixture postgres, DbContextOptions<AppDbContext> options) : IAsyncDisposable
    {
        public AppDbContext Db { get; } = new(options, new IRacingDataScope(DataProvenance.Demo));
        public Guid User { get; } = Guid.NewGuid();
        public List<Guid> Weeks { get; } = [];
        public Guid FieldPurpose { get; set; }
        public PercentileRefresh Refresh => new(Db, TimeProvider.System, new UnavailableDriverEnforcementJournal());
        public AppDbContext Open(params IInterceptor[] interceptors) => new(postgres.WithInterceptors(options, interceptors), new IRacingDataScope(DataProvenance.Demo));
        public async Task WriteAsync(int week, int car, double[] laps, bool subject = true)
        {
            await using var writer = Open();
            var copies = new EvidenceCopyLifecycle(writer, TimeProvider.System);
            var receipt = await copies.CaptureAsync(FieldPurpose, EvidenceCopyKind.OfficialField, $"field:{week}:{car}", ct: Ct);
            var id = week * 100 + car;
            await copies.CommitAsync(receipt, new OfficialFieldBatch(new() { Id = id, SeasonId = 1,
                RaceWeekIndex = week, WeekId = Weeks[week], TrackId = 1, OfficialSession = true, StartTime = DateTimeOffset.UtcNow },
                laps.Select((lap, index) => new SubsessionResult { SubsessionId = id, CustId = index == 0 && subject ? DemoData.DriverCustId : 2000 + index,
                    CarId = car, CarClassId = 1, BestLapSeconds = lap }).ToArray()), Ct);
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class Commands : DbCommandInterceptor
    {
        public int Total { get; private set; }
        public int Selects { get; private set; }
        public void Reset() { Total = 0; Selects = 0; }
        private void Note(DbCommand command) { Total++; if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)) Selects++; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        { Note(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        { Note(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<object> result, CancellationToken ct = default)
        { Note(command); return ValueTask.FromResult(result); }
    }
    private sealed class DemoGateFault(CancellationTokenSource? cancellation, bool failUnlock) : DbCommandInterceptor
    {
        public bool Reached { get; private set; }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (failUnlock && command.CommandText.Contains("pg_advisory_unlock(", StringComparison.Ordinal))
            { Reached = true; throw new InvalidOperationException("Controlled uncertain unlock."); }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData data,
            int result, CancellationToken ct = default)
        {
            if (!failUnlock && command.CommandText.Contains("pg_advisory_lock(", StringComparison.Ordinal))
            { Reached = true; cancellation!.Cancel(); ct.ThrowIfCancellationRequested(); }
            return ValueTask.FromResult(result);
        }
    }
    private sealed class HistoryCommands : DbCommandInterceptor
    {
        public int Total { get; private set; }
        public int Selects { get; private set; }
        public int SourceQueries { get; private set; }
        public int SourceRows { get; private set; }
        public int SelectedRows { get; private set; }
        public void Reset() { Total = Selects = SourceQueries = SourceRows = SelectedRows = 0; }
        private void Note(DbCommand command)
        { Total++; if (IsSelect(command)) Selects++; }
        private static bool IsSelect(DbCommand command) => command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        { Note(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        { Note(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<object> result, CancellationToken ct = default)
        { Note(command); return ValueTask.FromResult(result); }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data, DbDataReader result, CancellationToken ct = default)
        {
            var selected = IsSelect(command);
            // Actual complete Field projections include Driver/lap columns and marker versions;
            // DISTINCT Car discovery and metadata/authority queries are counted separately above.
            var source = selected && command.CommandText.Contains("\"CustId\",", StringComparison.Ordinal)
                && command.CommandText.Contains("\"BestLapSeconds\",", StringComparison.Ordinal)
                && command.CommandText.Contains("\"Version\"", StringComparison.Ordinal);
            if (source) SourceQueries++;
            return ValueTask.FromResult<DbDataReader>(new CountingReader(result, () => { if (selected) SelectedRows++; if (source) SourceRows++; }));
        }
    }
    private sealed class CountingReader(DbDataReader inner, Action read) : DbDataReader
    {
        public override bool Read() { var success = inner.Read(); if (success) read(); return success; }
        public override async Task<bool> ReadAsync(CancellationToken ct) { var success = await inner.ReadAsync(ct); if (success) read(); return success; }
        public override bool NextResult() => inner.NextResult();
        public override Task<bool> NextResultAsync(CancellationToken ct) => inner.NextResultAsync(ct);
        public override object this[int ordinal] => inner[ordinal];
        public override object this[string name] => inner[name];
        public override int Depth => inner.Depth;
        public override int FieldCount => inner.FieldCount;
        public override bool HasRows => inner.HasRows;
        public override bool IsClosed => inner.IsClosed;
        public override int RecordsAffected => inner.RecordsAffected;
        public override bool GetBoolean(int ordinal) => inner.GetBoolean(ordinal);
        public override byte GetByte(int ordinal) => inner.GetByte(ordinal);
        public override long GetBytes(int ordinal, long offset, byte[]? buffer, int bufferOffset, int length) => inner.GetBytes(ordinal, offset, buffer, bufferOffset, length);
        public override char GetChar(int ordinal) => inner.GetChar(ordinal);
        public override long GetChars(int ordinal, long offset, char[]? buffer, int bufferOffset, int length) => inner.GetChars(ordinal, offset, buffer, bufferOffset, length);
        public override string GetDataTypeName(int ordinal) => inner.GetDataTypeName(ordinal);
        public override DateTime GetDateTime(int ordinal) => inner.GetDateTime(ordinal);
        public override decimal GetDecimal(int ordinal) => inner.GetDecimal(ordinal);
        public override double GetDouble(int ordinal) => inner.GetDouble(ordinal);
        public override Type GetFieldType(int ordinal) => inner.GetFieldType(ordinal);
        public override float GetFloat(int ordinal) => inner.GetFloat(ordinal);
        public override Guid GetGuid(int ordinal) => inner.GetGuid(ordinal);
        public override short GetInt16(int ordinal) => inner.GetInt16(ordinal);
        public override int GetInt32(int ordinal) => inner.GetInt32(ordinal);
        public override long GetInt64(int ordinal) => inner.GetInt64(ordinal);
        public override string GetName(int ordinal) => inner.GetName(ordinal);
        public override int GetOrdinal(string name) => inner.GetOrdinal(name);
        public override string GetString(int ordinal) => inner.GetString(ordinal);
        public override object GetValue(int ordinal) => inner.GetValue(ordinal);
        public override int GetValues(object[] values) => inner.GetValues(values);
        public override bool IsDBNull(int ordinal) => inner.IsDBNull(ordinal);
        public override T GetFieldValue<T>(int ordinal) => inner.GetFieldValue<T>(ordinal);
        public override Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken ct) => inner.GetFieldValueAsync<T>(ordinal, ct);
        public override IEnumerator GetEnumerator() => ((IEnumerable)inner).GetEnumerator();
        public override void Close() => inner.Close();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        public override ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    private sealed class AfterSourceRead(Func<Task> action) : DbCommandInterceptor
    {
        public bool Reached { get; private set; }
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data, DbDataReader result, CancellationToken ct = default)
        {
            if (!Reached && command.CommandText.Contains("FROM iracing.\"RaceEvidenceResults\"", StringComparison.Ordinal))
            { Reached = true; await action(); }
            return result;
        }
    }
    private sealed class AfterCalendarRead(Func<Task> action) : DbCommandInterceptor
    {
        private int reads;
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data, DbDataReader result, CancellationToken ct = default)
        {
            if (command.CommandText.Contains("FROM iracing.\"Weeks\"", StringComparison.Ordinal) && ++reads == 2) await action();
            return result;
        }
    }
    private static void Artifact(string name, object value)
    {
        var root = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (!File.Exists(Path.Combine(root.FullName, "ApexRacers.sln")) && root.Parent is { } parent) root = parent;
        var directory = Path.Combine(root.FullName, "TestResults", name.StartsWith("discovered-", StringComparison.Ordinal) ? "428-history" : "427-refresh"); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }
}
