using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using ApexRacers.Tests.Lifecycle;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace ApexRacers.Tests.Publication;

internal sealed class PublicationLedgerTopology(string primary, string independent, Guid epoch, PostgreSqlFixture fixture,
    LifecycleProcess first, LifecycleProcess second, CancellationToken ct) : IAsyncDisposable
{
    private readonly List<LifecycleProcess> hosts = [first, second];
    public LifecycleProcess First { get; private set; } = first;
    public LifecycleProcess Second { get; } = second;
    public ControlledPublicationHistory History { get; } = new(independent, epoch, new LifecycleGates());
    public CancellationToken Token => ct;
    public string IndependentConnection => independent;
    public AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(primary).Options, new IRacingDataScope(DataProvenance.Demo));
    public PublicationReleaseStore Store(AppDbContext db) => new(db, TimeProvider.System, History, new FiniteCompositionReview(), epoch);
    public async Task<HttpResponseMessage> Read(string id, PublicationPurpose purpose = PublicationPurpose.Aggregate,
        string actor = "recipient", int offset = 0, bool secondHost = false, string? catalog = null)
    {
        return await (secondHost ? Second : First).Client.GetAsync($"/publish/{id}/{purpose}?actor={actor}&offset={offset}"
            + (catalog is null ? "" : "&catalog=" + Uri.EscapeDataString(catalog)),
            HttpCompletionOption.ResponseHeadersRead, ct);
    }
    public async Task Control(string path, bool secondHost = false)
    {
        using var response = await (secondHost ? Second : First).ControlClient.PostAsync("/control/" + path, null, ct);
        response.EnsureSuccessStatusCode();
    }
    public async Task Wait(string id, string phase, bool secondHost = false)
    {
        using var response = await (secondHost ? Second : First).ControlClient.GetAsync($"/control/wait/{id}/{phase}", ct);
        response.EnsureSuccessStatusCode();
    }
    public async Task Reconcile()
    {
        using var response = await Second.ControlClient.PostAsync("/reconcile", null, ct);
        response.EnsureSuccessStatusCode();
        Assert.True(await response.Content.ReadFromJsonAsync<bool>(ct));
    }
    public async Task WaitForCoordinationWaiter()
    {
        await using var connection = new NpgsqlConnection(primary);
        await connection.OpenAsync(ct);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        while (true)
        {
            await using var command = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_locks WHERE locktype='advisory' AND objid=370 AND NOT granted AND database=(SELECT oid FROM pg_database WHERE datname=current_database()))", connection);
            if ((bool)(await command.ExecuteScalarAsync(deadline.Token))!) return;
            await Task.Delay(10, deadline.Token);
        }
    }
    public async Task ChangeEvidence(Func<ApexRacers.Api.Services.ControlledCohortSource, ApexRacers.Api.Services.ControlledCohortSource> change)
    {
        await using var db = Db();
        var cache = await db.ExternalDataCaches.AsNoTracking().SingleAsync(c => c.CacheKey == CohortActors.CacheKey, ct);
        var source = JsonSerializer.Deserialize<ApexRacers.Api.Services.ControlledCohortSource>(cache.Payload)!;
        await CohortActors.SeedEvidenceAsync(db, change(source), ct);
    }
    public async Task<DriverLifecycleOutcome> Transition(int customerId = 1, DriverLifecycleKind kind = DriverLifecycleKind.WithdrawSharing)
    {
        await using var db = Db();
        var scope = new DriverScope(CohortActors.User(customerId), customerId, DataProvenance.Demo);
        var service = new ApexRacers.Api.Services.DriverAuthorization(new(db, TimeProvider.System), History,
            new UnavailableDriverOwnershipProof(), TimeProvider.System);
        return await service.TransitionAsync(scope, kind, Guid.NewGuid(), ct);
    }
    public async Task RestartFirst()
    {
        await First.StopAsync(ct);
        First = await LifecycleProcess.StartCohortAsync(primary, independent, epoch, ct);
        hosts.Add(First);
    }
    public async Task<string> Backup()
    {
        var db = new NpgsqlConnectionStringBuilder(primary);
        var path = "/tmp/publication374_" + Guid.NewGuid().ToString("N") + ".sql";
        var result = await fixture.Container.ExecAsync(["pg_dump", "--clean", "--if-exists", "--no-owner", "-U", db.Username!, "-d", db.Database!, "-f", path], ct);
        Assert.Equal(0, result.ExitCode);
        return path;
    }
    public async Task Restore(string path)
    {
        Assert.StartsWith("/tmp/publication374_", path);
        var db = new NpgsqlConnectionStringBuilder(primary);
        Assert.StartsWith("apexracers_ledger_primary_", db.Database);
        var result = await fixture.Container.ExecAsync(["psql", "-v", "ON_ERROR_STOP=1", "-U", db.Username!, "-d", db.Database!, "-f", path], ct);
        Assert.Equal(0, result.ExitCode);
    }

    public static async Task Run(PostgreSqlFixture fixture, string scenario, Func<PublicationLedgerTopology, Task> exercise)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        var ct = deadline.Token;
        var primary = await Create(fixture, "apexracers_ledger_primary_", ct);
        var independent = await Create(fixture, "apexracers_ledger_history_", ct);
        var epoch = Guid.NewGuid();
        var history = new ControlledPublicationHistory(independent, epoch, new LifecycleGates());
        await history.InitializeGenesisAsync(ct);
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(primary).Options, new IRacingDataScope(DataProvenance.Demo)))
        {
            await db.Database.MigrateAsync(ct);
            db.Users.AddRange(new[] { CohortActors.Owner, CohortActors.OtherOwner, CohortActors.Consenting, CohortActors.Recipient }
                .Select(id => new ApplicationUser { Id = id, DisplayName = "Synthetic fixture account", EmailConfirmed = true }));
            await db.SaveChangesAsync(ct);
            var grants = new DriverAuthorityStore(db, TimeProvider.System);
            foreach (var customer in new[] { 1, 2, 7 })
                await grants.GrantAsync(new(Guid.NewGuid(), new(CohortActors.User(customer), customer, DataProvenance.Demo),
                    DateTimeOffset.UtcNow.AddSeconds(-1), "controlled-synthetic-ledger-proof-v1", customer == 7 ? "Synthetic Consenting Driver" : "Synthetic owner " + customer),
                    new(DriverAuthorizationPolicy.PersonalConsentVersion, customer == 7 ? DriverAuthorizationPolicy.SharingConsentVersion : null), history, ct);
            await CohortActors.SeedEvidenceAsync(db, CohortActors.Source(), ct);
            var seededCopy = await db.ExternalDataCaches.SingleAsync(c => c.CacheKey == CohortActors.CacheKey, ct);
            await db.Database.ExecuteSqlRawAsync("CREATE TABLE controlled_fixture_genesis(copy_id uuid PRIMARY KEY)", ct);
            await db.Database.ExecuteSqlAsync($"INSERT INTO controlled_fixture_genesis VALUES ({seededCopy.EvidenceCopyId})", ct);
        }
        var first = await LifecycleProcess.StartCohortAsync(primary, independent, epoch, ct);
        LifecycleProcess second;
        try { second = await LifecycleProcess.StartCohortAsync(primary, independent, epoch, ct); }
        catch { await first.DisposeAsync(); throw; }
        await using var test = new PublicationLedgerTopology(primary, independent, epoch, fixture, first, second, ct);
        var passed = false;
        try { await exercise(test); passed = true; }
        finally
        {
            var output = Path.Combine("TestResults", "publication-ledger");
            Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(Path.Combine(output, scenario + ".json"), JsonSerializer.Serialize(new
            {
                SchemaVersion = 1,
                Scenario = scenario,
                Passed = passed,
                FixtureReview = FiniteCompositionReview.ReviewVersion,
                SuiteObligations = new[] { "PUB-04", "PUB-07", "PUB-08", "PUB-09", "PUB-10", "HTTP-01", "HTTP-02", "HTTP-03", "RESTORE-02" },
                Hosts = test.hosts.Select(h => new { h.ProcessId, h.Incarnation }),
                Boundary = "Controlled synthetic two-Kestrel writers and separate PostgreSQL history; no production approval or deployed restore certification."
            }, new JsonSerializerOptions { WriteIndented = true }), CancellationToken.None);
        }
    }
    private static async Task<string> Create(PostgreSqlFixture fixture, string prefix, CancellationToken ct)
    {
        var name = prefix + Guid.NewGuid().ToString("N");
        await using var c = new NpgsqlConnection(fixture.Container.GetConnectionString());
        await c.OpenAsync(ct);
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", c);
        await command.ExecuteNonQueryAsync(ct);
        return new NpgsqlConnectionStringBuilder(fixture.Container.GetConnectionString()) { Database = name, Host = "127.0.0.1", Pooling = false }.ConnectionString;
    }
    public async ValueTask DisposeAsync()
    {
        foreach (var host in hosts) await host.DisposeAsync();
    }
    public static async Task Denied(HttpResponseMessage response, CancellationToken ct)
    {
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var body = await response.Content.ReadAsStringAsync(ct);
        foreach (var field in new[] { "Synthetic Consenting Driver", "hiddenGroups", "namedRows", "ownerAnalytics", "lowerInclusive", "percentileRank" })
            Assert.DoesNotContain(field, body);
    }
}
