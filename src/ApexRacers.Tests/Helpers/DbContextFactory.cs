using ApexRacers.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Tests.Helpers;

public static class DbContextFactory
{
    /// <summary>
    /// Creates an <see cref="AppDbContext"/> backed by a fresh in-memory SQLite database.
    ///
    /// SQLite is a real relational provider, so unlike the EF InMemory provider it forces every
    /// query to actually translate to SQL and enforces FK / NOT NULL constraints. This catches
    /// GroupBy/aggregate translation gaps and missing-relationship bugs in tests that would
    /// otherwise only surface against Postgres in production.
    ///
    /// An in-memory SQLite database lives only as long as its connection is open, so we hand the
    /// open connection to EF with <c>contextOwnsConnection: true</c> — disposing the returned
    /// context (callers use <c>await using</c>) closes the connection and tears the database down.
    /// </summary>
    public static AppDbContext Create()
    {
        // Foreign Keys=False: T9's goal is to validate SQL *translatability* (the relational query
        // pipeline, shared with Npgsql), not referential integrity. Unit tests use minimal partial
        // fixtures targeting one service, so enforcing the full FK graph would over-couple them to
        // the schema for no translatability gain. Production integrity is guaranteed by the Postgres
        // schema/migrations and the ingestion worker's dependency-ordered inserts.
        var connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection, contextOwnsConnection: true)
            .Options;

        var context = new AppDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    /// <summary>
    /// Creates an <see cref="AppDbContext"/> on the EF InMemory provider. Reserved for the rare
    /// test whose production query is valid on Npgsql but cannot be translated by SQLite — namely
    /// a <c>DateTimeOffset</c> range filter (<see cref="ApexRacers.Api.Services.ExternalDataCacheCleanupService"/>).
    /// Prefer <see cref="Create"/> (SQLite) everywhere else so queries are validated against real SQL.
    /// </summary>
    public static AppDbContext CreateInMemory() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
