using System.Data.Common;
using System.Text.Json;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Core.PercentileHistory;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

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
        var directory = Path.Combine(root.FullName, "TestResults", "427-refresh"); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }
}
