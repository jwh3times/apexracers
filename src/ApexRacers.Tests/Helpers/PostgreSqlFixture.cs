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
    protected override PostgreSqlBuilder Configure() =>
        new PostgreSqlBuilder("postgres:18.0-alpine");

    public async Task<AppDbContext> CreateDbContextAsync(
        CancellationToken ct,
        params IInterceptor[] interceptors)
    {
        var options = await CreateOptionsAsync(ct, interceptors);
        return new AppDbContext(options);
    }

    public DbContextOptions<AppDbContext> CreateOptions(params IInterceptor[] interceptors)
    {
        var databaseName = $"apexracers_test_{Guid.NewGuid():N}";
        using (var connection = new NpgsqlConnection(Container.GetConnectionString()))
        {
            connection.Open();
            using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
            command.ExecuteNonQuery();
        }

        var options = BuildOptions(databaseName, interceptors);
        using var context = new AppDbContext(options);
        context.Database.EnsureCreated();
        return options;
    }

    public async Task<DbContextOptions<AppDbContext>> CreateOptionsAsync(
        CancellationToken ct,
        params IInterceptor[] interceptors)
    {
        var databaseName = $"apexracers_test_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(Container.GetConnectionString()))
        {
            await connection.OpenAsync(ct);
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
            await command.ExecuteNonQueryAsync(ct);
        }

        var options = BuildOptions(databaseName, interceptors);
        await using var context = new AppDbContext(options);
        await context.Database.EnsureCreatedAsync(ct);
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
        // Scanned rather than looked up: FindExtension matches the exact type, and what is
        // registered is the provider's own NpgsqlOptionsExtension, not the abstract relational base.
        var connectionString = options.Extensions
            .OfType<RelationalOptionsExtension>()
            .Select(extension => extension.ConnectionString)
            .First(value => value is not null)!;
        return new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .AddInterceptors(interceptors)
            .Options;
    }

    private DbContextOptions<AppDbContext> BuildOptions(
        string databaseName,
        IReadOnlyCollection<IInterceptor> interceptors)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(Container.GetConnectionString())
        {
            Database = databaseName,
            Pooling = false,
        }.ConnectionString;
        var builder = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString);
        if (interceptors.Count > 0)
            builder.AddInterceptors(interceptors);

        var options = builder.Options;
        return options;
    }
}
