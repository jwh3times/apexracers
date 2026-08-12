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
    /// query to actually translate to SQL and enforces relational column constraints. Foreign-key
    /// enforcement is deliberately disabled below so focused service fixtures need not construct
    /// the entire production relationship graph. This still catches GroupBy/aggregate translation
    /// gaps that would otherwise only surface against PostgreSQL in production.
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
    /// One in-memory SQLite database that several <see cref="AppDbContext"/> instances share, for
    /// tests that need two callers racing the same rows — a single context serializes everything
    /// through one change tracker and so cannot express a concurrent write at all.
    ///
    /// The connection is owned here rather than by any context, because in-memory SQLite drops the
    /// database when its last connection closes: letting a context own it would tear the database
    /// down as soon as that one context was disposed.
    /// </summary>
    public static SharedSqliteDatabase CreateShared() => new();

    public sealed class SharedSqliteDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        internal SharedSqliteDatabase()
        {
            _connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
            _connection.Open();
            using var seed = NewContext();
            seed.Database.EnsureCreated();
        }

        public AppDbContext NewContext() =>
            new(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(_connection, contextOwnsConnection: false)
                .Options);

        public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
    }
}
