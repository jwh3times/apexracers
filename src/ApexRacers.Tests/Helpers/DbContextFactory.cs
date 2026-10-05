using ApexRacers.Core;
using ApexRacers.Core.Models;
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
    public static AppDbContext Create(DataProvenance provenance = DataProvenance.Real)
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

        var context = new AppDbContext(options, new IRacingDataScope(provenance));
        context.Database.EnsureCreated();
        InstallStoredEvidenceFixtures(context);
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

        public AppDbContext NewContext(DataProvenance provenance = DataProvenance.Real)
        {
            var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(_connection, contextOwnsConnection: false)
                .Options, new IRacingDataScope(provenance));
            InstallStoredEvidenceFixtures(context);
            return context;
        }

        public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
    }

    // These unit fixtures describe already-retained synthetic rows for legacy query/calculation tests.
    // They are not acquisition authority: PostgreSQL lifecycle tests use ordinary AppDbContext,
    // migrations and controlled issuers, and exercise rejection of missing/fabricated write metadata.
    internal static void InstallStoredEvidenceFixtures(AppDbContext context)
    {
        var purposes = new Dictionary<DataProvenance, EvidencePurpose>();
        var fields = new Dictionary<(DataProvenance, int), EvidenceCopyMarker>();
        context.ChangeTracker.Tracked += (_, args) =>
        {
            if (args.Entry.State != EntityState.Added || args.Entry.Entity is not IManagedEvidence copy || copy.EvidenceCopyId is not null) return;
            if (purposes.Values.Any(p => context.Entry(p).State == EntityState.Detached))
            { purposes.Clear(); fields.Clear(); }
            var provenance = copy.Provenance == DataProvenance.Unknown ? context.Provenance : copy.Provenance;
            if (provenance == DataProvenance.Unknown) return;
            if (!purposes.TryGetValue(provenance, out var purpose))
            {
                purpose = new()
                {
                    Id = Guid.NewGuid(),
                    Provenance = provenance,
                    Kind = EvidencePurposeKind.IndependentOfficial,
                    CreatedAt = DateTimeOffset.UtcNow.AddYears(-10)
                };
                purposes[provenance] = purpose;
                context.EvidencePurposes.Add(purpose);
            }
            var kind = copy switch
            {
                ExternalDataCache => EvidenceCopyKind.MappedCache,
                Subsession or SubsessionResult => EvidenceCopyKind.OfficialField,
                SeasonCarBop => EvidenceCopyKind.Bop,
                Rival => EvidenceCopyKind.Follow,
                CarPercentileResult => EvidenceCopyKind.PersonalDerivative,
                _ => EvidenceCopyKind.AuthorizedName,
            };
            var fieldId = copy switch { Subsession s => s.Id, SubsessionResult r => r.SubsessionId, _ => 0 };
            if (kind != EvidenceCopyKind.OfficialField || !fields.TryGetValue((provenance, fieldId), out var marker))
            {
                marker = new EvidenceCopyMarker
                {
                    Id = Guid.NewGuid(),
                    PurposeId = purpose.Id,
                    Provenance = provenance,
                    Generation = 1,
                    Version = 1,
                    Kind = kind,
                    KeyHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
                    OriginalAcquiredAt = copy is ExternalDataCache cache ? cache.FetchedAt : purpose.CreatedAt
                };
                if (copy is ExternalDataCache cached)
                {
                    marker.ExpiresAt = cached.ExpiresAt;
                    marker.RemovalDueAt = EvidenceRetention.MappedRemovalDueAt(cached.ExpiresAt);
                }
                context.EvidenceCopyMarkers.Add(marker);
                if (kind == EvidenceCopyKind.OfficialField) fields[(provenance, fieldId)] = marker;
            }
            copy.EvidenceCopyId = marker.Id;
        };
        context.SavingChanges += (_, _) =>
        {
            foreach (var entry in context.ChangeTracker.Entries<Week>().ToArray())
            {
                var week = entry.Entity;
                foreach (var provenance in new[] { DataProvenance.Real, DataProvenance.Demo })
                {
                    if (provenance == DataProvenance.Real ? week.WeatherSummaryJson is null || week.WeatherEvidenceCopyId is not null
                        : week.DemoWeatherSummaryJson is null || week.DemoWeatherEvidenceCopyId is not null) continue;
                    if (!purposes.TryGetValue(provenance, out var purpose) || context.Entry(purpose).State == EntityState.Detached)
                    {
                        purpose = new()
                        {
                            Id = Guid.NewGuid(),
                            Provenance = provenance,
                            Kind = EvidencePurposeKind.IndependentOfficial,
                            CreatedAt = DateTimeOffset.UtcNow.AddYears(-10)
                        };
                        purposes[provenance] = purpose;
                        context.EvidencePurposes.Add(purpose);
                    }
                    var marker = new EvidenceCopyMarker
                    {
                        Id = Guid.NewGuid(),
                        PurposeId = purpose.Id,
                        Provenance = provenance,
                        Generation = 1,
                        Version = 1,
                        Kind = EvidenceCopyKind.Weather,
                        KeyHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
                        OriginalAcquiredAt = purpose.CreatedAt
                    };
                    context.EvidenceCopyMarkers.Add(marker);
                    if (provenance == DataProvenance.Real) week.WeatherEvidenceCopyId = marker.Id;
                    else week.DemoWeatherEvidenceCopyId = marker.Id;
                }
            }
        };
    }
}
