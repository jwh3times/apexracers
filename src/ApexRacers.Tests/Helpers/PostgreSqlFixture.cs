using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.Xunit;
using Xunit;
using Xunit.Sdk;

namespace ApexRacers.Tests.Helpers;

[CollectionDefinition(Name)]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "PostgreSQL integration";
}

/// <summary>
/// Shares one pinned PostgreSQL container across the provider-specific test collection while
/// creating a fresh database for every test. The database-per-test boundary preserves isolation
/// even when a test fails before disposing its context; the container removes all databases when
/// the collection completes. <c>EnsureCreated</c> deliberately builds the current EF model: these
/// tests validate Npgsql translation, PostgreSQL transactions, and relational constraints, while
/// migration application remains the startup/deployment path's responsibility.
/// </summary>
public sealed class PostgreSqlFixture(IMessageSink messageSink)
    : ContainerFixture<PostgreSqlBuilder, PostgreSqlContainer>(messageSink)
{
    private readonly Lock poolGate = new();
    private readonly HashSet<string> ownedPools = [];
    private string? poolOwner;

    protected override async ValueTask InitializeAsync()
    {
        FixtureCostProbe.TestcontainersSessionResource();
        await FixtureCostProbe.MeasureAsync("container.initialize", async () => await base.InitializeAsync());
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APEXRACERS_COST_PROBE_DIR")))
            FixtureCostProbe.ContainerResource(Container.Id, Container.GetMappedPublicPort(5432), "ready");
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        AdoptPoolOwner(null);
        var probeEnabled = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APEXRACERS_COST_PROBE_DIR"));
        var containerId = probeEnabled ? Container.Id : "";
        if (probeEnabled) FixtureCostProbe.ContainerResource(containerId, 0, "disposing");
        await FixtureCostProbe.MeasureAsync("container.dispose", async () => await base.DisposeAsyncCore());
        if (probeEnabled) FixtureCostProbe.ContainerResource(containerId, 0, "disposed");
    }

    protected override PostgreSqlBuilder Configure() =>
        new PostgreSqlBuilder("postgres:18.0-alpine");

    public async Task<AppDbContext> CreateDbContextAsync(
        CancellationToken ct,
        params IInterceptor[] interceptors)
    {
        var options = await CreateOptionsAsync(ct, interceptors);
        var context = new AppDbContext(options, new ApexRacers.Core.IRacingDataScope(ApexRacers.Core.DataProvenance.Real));
        DbContextFactory.InstallStoredEvidenceFixtures(context);
        return context;
    }

    public DbContextOptions<AppDbContext> CreateOptions(params IInterceptor[] interceptors)
    {
        using var optionsProbe = FixtureCostProbe.Begin("fixture.options");
        var databaseName = $"apexracers_test_{Guid.NewGuid():N}";
        using (var connection = new NpgsqlConnection(Container.GetConnectionString()))
        {
            using (FixtureCostProbe.Begin("connection.open")) connection.Open();
            using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
            using (FixtureCostProbe.Begin("database.create")) command.ExecuteNonQuery();
        }

        var options = BuildOptions(databaseName, interceptors);
        using var context = new AppDbContext(options);
        using (FixtureCostProbe.Begin("schema.ensure-created")) context.Database.EnsureCreated();
        return options;
    }

    public async Task<DbContextOptions<AppDbContext>> CreateOptionsAsync(
        CancellationToken ct,
        params IInterceptor[] interceptors)
    {
        using var optionsProbe = FixtureCostProbe.Begin("fixture.options");
        var databaseName = $"apexracers_test_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(Container.GetConnectionString()))
        {
            await FixtureCostProbe.MeasureAsync("connection.open", () => connection.OpenAsync(ct));
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
            await FixtureCostProbe.MeasureAsync("database.create", async () => await command.ExecuteNonQueryAsync(ct));
        }

        var options = BuildOptions(databaseName, interceptors);
        await using var context = new AppDbContext(options);
        await FixtureCostProbe.MeasureAsync("schema.ensure-created", async () => await context.Database.EnsureCreatedAsync(ct));
        return options;
    }

    /// <summary>
    /// Rebuilds options over the <em>same</em> database with different interceptors. Needed when a
    /// test has to reach one database through two contexts that observe it differently — an
    /// interceptor that writes through a plain context cannot be attached to the very options it
    /// writes through, and <see cref="CreateOptionsAsync"/> would hand back a fresh database.
    /// </summary>
    public DbContextOptions<AppDbContext> WithInterceptors(
        DbContextOptions<AppDbContext> options,
        params IInterceptor[] interceptors)
    {
        return new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString(options))
            .AddInterceptors(interceptors)
            .Options;
    }

    // Scanned rather than looked up: FindExtension matches the exact type, and what is
    // registered is the provider's own NpgsqlOptionsExtension, not the abstract relational base.
    private static string ConnectionString(DbContextOptions<AppDbContext> options) =>
        options.Extensions
            .OfType<RelationalOptionsExtension>()
            .Select(extension => extension.ConnectionString)
            .First(value => value is not null)!;

    /// <summary>
    /// Clears every pool an earlier test created and makes <paramref name="owner"/> the test that
    /// owns pools from here on. The collection runs its tests one at a time, so the first options
    /// request from a different test means the earlier test has finished with its databases.
    /// </summary>
    internal void AdoptPoolOwner(string? owner)
    {
        string[] released;
        lock (poolGate)
        {
            if (poolOwner == owner && owner is not null) return;
            released = [.. ownedPools];
            ownedPools.Clear();
            poolOwner = owner;
        }
        foreach (var connectionString in released)
        {
            using var connection = new NpgsqlConnection(connectionString);
            NpgsqlConnection.ClearPool(connection);
        }
    }

    /// <summary>
    /// Counts the physical sessions a database has received once every one of them has ended.
    /// A backend reports its session statistics as it exits, before it leaves
    /// <c>pg_stat_activity</c>, so the count read after the last backend is gone is complete.
    /// </summary>
    internal async Task<long> SessionsAfterBackendsExitAsync(string database, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        await using var connection = new NpgsqlConnection(Container.GetConnectionString());
        await connection.OpenAsync(deadline.Token);
        await using var active = new NpgsqlCommand("SELECT count(*) FROM pg_stat_activity WHERE datname = @database", connection);
        active.Parameters.AddWithValue("database", database);
        while ((long)(await active.ExecuteScalarAsync(deadline.Token))! > 0)
            await Task.Delay(TimeSpan.FromMilliseconds(100), deadline.Token);
        await using var sessions = new NpgsqlCommand("SELECT sessions FROM pg_stat_database WHERE datname = @database", connection);
        sessions.Parameters.AddWithValue("database", database);
        return (long)(await sessions.ExecuteScalarAsync(deadline.Token))!;
    }

    private DbContextOptions<AppDbContext> BuildOptions(
        string databaseName,
        IReadOnlyCollection<IInterceptor> interceptors)
    {
        // Pooled per test database (issue #391): one physical connection per open left thousands
        // of client sockets in TIME_WAIT. AdoptPoolOwner bounds the idle pools to the current test.
        var connectionString = new NpgsqlConnectionStringBuilder(Container.GetConnectionString())
        {
            Database = databaseName,
        }.ConnectionString;
        AdoptPoolOwner(TestContext.Current.Test?.UniqueID);
        lock (poolGate) ownedPools.Add(connectionString);
        var builder = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString);
        if (interceptors.Count > 0)
            builder.AddInterceptors(interceptors);

        var options = builder.Options;
        return options;
    }
}
