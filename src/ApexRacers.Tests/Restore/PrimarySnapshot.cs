using System.Reflection;
using System.Text.Json;
using ApexRacers.Tests.Helpers;
using Npgsql;
using Xunit;

namespace ApexRacers.Tests.Restore;

/// <summary>Real pg_dump/psql snapshots, limited to uniquely named, disposable loopback databases.</summary>
internal sealed record PrimarySnapshot(string Path, string Sha256, string Database)
{
    public static async Task<PrimarySnapshot> CaptureAsync(PostgreSqlFixture fixture, string connection, CancellationToken ct)
    {
        var db = RequireDisposable(connection);
        var path = "/tmp/driver377_" + Guid.NewGuid().ToString("N") + ".sql";
        var dump = await fixture.Container.ExecAsync(["pg_dump", "--clean", "--if-exists", "--no-owner", "-U", db.Username!, "-d", db.Database!, "-f", path], ct);
        Assert.Equal(0, dump.ExitCode);
        var hash = await fixture.Container.ExecAsync(["sha256sum", path], ct);
        Assert.Equal(0, hash.ExitCode);
        return new(path, hash.Stdout.Split(' ')[0], db.Database!);
    }

    public async Task RestoreAsync(PostgreSqlFixture fixture, string connection, CancellationToken ct, bool replaceSchemas = false)
    {
        var db = RequireDisposable(connection);
        Assert.Equal(Database, db.Database);
        Assert.StartsWith("/tmp/driver377_", Path);
        if (replaceSchemas)
        {
            var clear = await fixture.Container.ExecAsync(["psql", "-v", "ON_ERROR_STOP=1", "-U", db.Username!, "-d", db.Database!, "-c", "DROP SCHEMA IF EXISTS identity, iracing, public CASCADE"], ct);
            Assert.Equal(0, clear.ExitCode);
        }
        var restore = await fixture.Container.ExecAsync(["psql", "-v", "ON_ERROR_STOP=1", "-U", db.Username!, "-d", db.Database!, "-f", Path], ct);
        Assert.Equal(0, restore.ExitCode);
    }

    private static NpgsqlConnectionStringBuilder RequireDisposable(string connection)
    {
        var db = new NpgsqlConnectionStringBuilder(connection);
        if (db.Host is not ("127.0.0.1" or "localhost") || db.Database is not { } name
            || !new[] { "apexracers_restore_legacy_", "apexracers_lifecycle_primary_", "apexracers_reference_primary_" }.Any(p => name.StartsWith(p, StringComparison.Ordinal)))
            throw new InvalidOperationException("A unique disposable primary restore database is required.");
        return db;
    }

    public async Task RecordAsync(string scenario, object observations, bool passed)
    {
        var directory = System.IO.Path.Combine(LegacyApiProcess.RepositoryRoot, "TestResults", "driver-restore");
        Directory.CreateDirectory(directory);
        var sourceHashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.GetFiles(System.IO.Path.Combine(LegacyApiProcess.RepositoryRoot, "src/ApexRacers.Tests/Restore"), "*.cs")
            .Concat(new[] { "src/ApexRacers.Data/EntityConfigurations/UploadedLapConfiguration.cs",
                "src/ApexRacers.Data/Migrations/20261009005518_FenceLegacyUploadedLapReaders.cs" }.Select(p => System.IO.Path.Combine(LegacyApiProcess.RepositoryRoot, p))))
        {
            var relative = System.IO.Path.GetRelativePath(LegacyApiProcess.RepositoryRoot, path).Replace('\\', '/');
            sourceHashes.Add(relative, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(path, CancellationToken.None))));
        }
        await File.WriteAllTextAsync(System.IO.Path.Combine(directory, scenario + ".json"), JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            Scenario = scenario,
            Passed = passed,
            Dataset = "driver-restore-synthetic-v1",
            Commit = typeof(PrimarySnapshot).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            LegacyCommit = LegacyApiProcess.LegacyCommit,
            PostgreSqlImage = "postgres:18.0-alpine",
            SnapshotSha256 = Sha256,
            PrimaryDatabase = Database,
            SourceSha256 = sourceHashes,
            Commands = new[] { "pg_dump --clean --if-exists --no-owner", "psql -v ON_ERROR_STOP=1 -f snapshot.sql" },
            Observations = observations,
            Boundary = "Fabricated identities/typed evidence; actual local migrations, processes, HTTP and physical PostgreSQL snapshots. Independent enforcement/history excluded from primary backup. No live authorization, provider processing, production removal or backup-expiry certification."
        }, new JsonSerializerOptions { WriteIndented = true }), CancellationToken.None);
    }
}
