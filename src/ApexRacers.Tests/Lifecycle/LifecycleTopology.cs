using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using ApexRacers.Tests.Publication;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace ApexRacers.Tests.Lifecycle;

internal sealed class LifecycleTopology(
    string primary, string journal, RehearsalDatabaseLink link, LifecycleProcess publisher,
    LifecycleProcess coordinator, CancellationToken ct) : IAsyncDisposable
{
    private readonly List<LifecycleProcess> hosts = [publisher, coordinator];
    private readonly List<object> histories = [];
    private readonly List<object> outcomes = [];
    private readonly List<string> faults = [];
    public LifecycleProcess Publisher { get; private set; } = publisher;
    public LifecycleProcess Coordinator { get; private set; } = coordinator;
    public CancellationToken CancellationToken => ct;
    public PersistedSyntheticJournal Journal { get; } = new(journal, new LifecycleGates());

    public AppDbContext OpenPrimary() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(primary).Options,
        new IRacingDataScope(DataProvenance.Demo));

    public async Task GrantAsync(bool sharing = false)
    {
        using var response = await Coordinator.Client.PostAsync("/grant?consent=" + (sharing ? "both" : "personal"), null, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<HttpResponseMessage> ReadAsync(string id, string? actor = null, bool onCoordinator = false,
        CancellationToken cancellationToken = default) =>
        await (onCoordinator ? Coordinator : Publisher).Client.GetAsync($"/private/{id}?actor={actor}",
            HttpCompletionOption.ResponseHeadersRead, cancellationToken == default ? ct : cancellationToken);

    public async Task<DriverLifecycleOutcome> TransitionAsync(Guid operationId, DriverLifecycleKind kind = DriverLifecycleKind.WithdrawPersonal)
    {
        using var response = await Coordinator.Client.PostAsync($"/transition/{operationId}/{kind}", null, ct);
        response.EnsureSuccessStatusCode();
        var result = (await response.Content.ReadFromJsonAsync<DriverLifecycleOutcome>(ct))!;
        outcomes.Add(new { Status = (int)response.StatusCode, Result = result });
        return result;
    }

    public Task HoldAsync(string id, string phase, bool onCoordinator = false) => PostControlAsync($"hold/{id}/{phase}", onCoordinator);
    public Task ReleaseAsync(string id, string phase, bool onCoordinator = false) => PostControlAsync($"release/{id}/{phase}", onCoordinator);
    public Task FaultAsync(string phase) => PostControlAsync("fault/" + phase, true);
    public Task ClearFaultAsync(string phase) => PostControlAsync("clear/" + phase, true);
    public Task FaultPublisherAsync(string phase) => PostControlAsync("fault/" + phase);
    public Task ClearPublisherFaultAsync(string phase) => PostControlAsync("clear/" + phase);
    public Task RetryCheckpointAsync(string id) => PostControlAsync("retry-checkpoint/" + id);

    public async Task WaitAsync(string id, string phase, bool onCoordinator = false)
    {
        using var response = await (onCoordinator ? Coordinator : Publisher).ControlClient.GetAsync($"/control/wait/{id}/{phase}", ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task PostControlAsync(string path, bool onCoordinator = false)
    {
        using var response = await (onCoordinator ? Coordinator : Publisher).ControlClient.PostAsync("/control/" + path, null, ct);
        response.EnsureSuccessStatusCode();
    }

    public void PartitionPrimary()
    {
        link.Partition();
        faults.Add("Publisher primary PostgreSQL link cut; HTTP and independent journal remain reachable.");
    }

    public void RestorePrimary()
    {
        link.Restore();
        faults.Add("Publisher primary PostgreSQL link restored; no inference of terminality.");
    }

    public async Task RestartBothAsync()
    {
        await CaptureAsync();
        await Publisher.StopAsync(ct);
        await Coordinator.StopAsync(ct);
        faults.Add("Both exact synthetic processes killed and OS exits observed; no checkpoint inferred.");
        Publisher = await LifecycleProcess.StartAsync(link.ConnectionString, journal, ct);
        Coordinator = await LifecycleProcess.StartAsync(primary, journal, ct);
        hosts.Add(Publisher);
        hosts.Add(Coordinator);
    }

    public async Task ExpireLeasesAsync()
    {
        await using var db = OpenPrimary();
        await db.DriverPublicationAdmissions.Where(a => a.TerminalAt == null)
            .ExecuteUpdateAsync(updates => updates.SetProperty(a => a.LeaseUntil, DateTimeOffset.UtcNow.AddMinutes(-10)), ct);
        faults.Add("Admission leases expired without terminal evidence.");
    }

    public async Task TerminateCheckpointSessionAsync(string id)
    {
        // CompleteAsync can finish an HTTP response before the handler's terminal checkpoint.
        // Never queue a control lookup behind that handler on its reused HTTP/1 connection.
        var pid = await Publisher.ControlClient.GetFromJsonAsync<int>("/control/backend/" + id, ct);
        await using var connection = new NpgsqlConnection(primary);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT pg_terminate_backend(@pid)", connection);
        command.Parameters.AddWithValue("pid", pid);
        Assert.True((bool)(await command.ExecuteScalarAsync(ct))!);
        faults.Add($"Actual PostgreSQL terminal-checkpoint backend {pid} terminated while HTTP host remained alive.");
    }

    public async Task<DriverLifecycleSnapshot> SnapshotAsync()
    {
        await using var db = OpenPrimary();
        return new DriverLifecycleSnapshot(
            await db.DriverAuthorizationGrants.AsNoTracking().ToArrayAsync(ct),
            await db.DriverLifecycleOperations.AsNoTracking().ToArrayAsync(ct),
            await db.DriverCopyCleanups.AsNoTracking().ToArrayAsync(ct),
            await db.DriverPublicationAdmissions.AsNoTracking().ToArrayAsync(ct),
            await db.DriverTrackedCopies.AsNoTracking().ToArrayAsync(ct));
    }

    private async Task CaptureAsync()
    {
        foreach (var host in new[] { Publisher, Coordinator })
        {
            if (host.HasExited)
                continue;
            var events = await host.ControlClient.GetFromJsonAsync<LifecycleEvent[]>("/control/events", ct);
            histories.Add(new { host.ProcessId, host.Incarnation, Events = events });
        }
    }

    public static async Task RunAsync(PostgreSqlFixture fixture, string scenario, string[] evidenceIds,
        Func<LifecycleTopology, Task> exercise)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        var ct = deadline.Token;
        var started = DateTimeOffset.UtcNow;
        var primary = await CreateDatabaseAsync(fixture, "apexracers_lifecycle_primary_", ct);
        var journal = await CreateDatabaseAsync(fixture, "apexracers_lifecycle_journal_", ct);
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(primary).Options))
        {
            await db.Database.EnsureCreatedAsync(ct);
            db.Users.AddRange(new ApplicationUser { Id = SyntheticLifecycleActors.Owner, DisplayName = "Synthetic owner", EmailConfirmed = true },
                new ApplicationUser { Id = SyntheticLifecycleActors.Other, DisplayName = "Synthetic recipient", EmailConfirmed = true });
            await db.SaveChangesAsync(ct);
        }
        await new PersistedSyntheticJournal(journal, new LifecycleGates()).InitializeAsync(ct);
        var link = RehearsalDatabaseLink.Create(primary);
        LifecycleProcess publisher;
        try { publisher = await LifecycleProcess.StartAsync(link.ConnectionString, journal, ct); }
        catch { await link.DisposeAsync(); throw; }
        LifecycleProcess coordinator;
        try { coordinator = await LifecycleProcess.StartAsync(primary, journal, ct); }
        catch { await publisher.DisposeAsync(); await link.DisposeAsync(); throw; }
        await using var topology = new LifecycleTopology(primary, journal, link, publisher, coordinator, ct);
        var passed = false;
        try
        {
            await exercise(topology).WaitAsync(TimeSpan.FromMinutes(2), ct);
            passed = true;
        }
        finally
        {
            try { await topology.CaptureAsync(); }
            catch (Exception error) when (!passed && error is HttpRequestException or OperationCanceledException)
            { topology.faults.Add("Final observation unavailable: " + error.GetType().Name); }
            object? state = null;
            try
            {
                var snapshot = await topology.SnapshotAsync();
                state = new
                {
                    GrantCount = snapshot.Grants.Length,
                    Grants = snapshot.Grants.Select(g => new { g.Revision, g.BindingActive, g.ProofValid,
                        HasPersonalConsent = g.PersonalConsentVersion is not null,
                        HasSharingConsent = g.SharingConsentVersion is not null, g.PersonalClosedAt, g.SharingClosedAt }),
                    Operations = snapshot.Operations.Select(o => new { o.Id, o.Kind, o.OriginalLossAt, o.AppliedRevision, o.PrimaryAppliedAt, o.CompletedAt }),
                    Cleanup = snapshot.Cleanup.Select(w => new { w.OperationId, w.Purpose, w.OriginalLossAt, w.DueAt, w.VerifiedRemovedAt }),
                    Admissions = snapshot.Admissions.Select(a => new { a.Id, a.Revision, a.Purpose, a.Incarnation, a.AdmittedAt, a.LeaseUntil, a.TerminalAt }),
                    CopyCount = snapshot.Copies.Length,
                    UnavailableCopyCount = snapshot.Copies.Count(c => c.UnavailableAt is not null),
                };
            }
            catch (Exception error) when (!passed && error is NpgsqlException or OperationCanceledException)
            { topology.faults.Add("Final primary state unavailable: " + error.GetType().Name); }
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root.Parent is not null && !File.Exists(Path.Combine(root.FullName, "global.json")))
                root = root.Parent;
            var directory = Path.Combine(root.FullName, "TestResults", "driver-lifecycle",
                $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfff}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, scenario + ".json"), JsonSerializer.Serialize(new
            {
                SchemaVersion = 1, Scenario = scenario, Result = passed ? "passed" : "failed", EvidenceIds = evidenceIds,
                Started = started, Finished = DateTimeOffset.UtcNow,
                Commit = typeof(LifecycleTopology).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                Runtime = RuntimeInformation.FrameworkDescription, Platform = RuntimeInformation.OSDescription,
                PostgreSqlImage = "postgres:18.0-alpine", Provenance = "Demo; controlled synthetic identities only",
                ProofAuthority = "controlled-synthetic-proof-v1; never provider ownership", PersonalConsentVersion = DriverAuthorizationPolicy.PersonalConsentVersion,
                SharingConsentVersion = DriverAuthorizationPolicy.SharingConsentVersion,
                Modules = new[] { "DriverAuthorization", "DriverAuthorityStore", "DriverPublication protected IActionResult" },
                Journal = "Separate PostgreSQL database persists intents independently of primary and both processes",
                Catalog = "No publication catalog admitted; bounded synthetic owner/name-only artifact",
                Hosts = topology.hosts.Select(h => new { h.ProcessId, h.Incarnation, Address = h.Client.BaseAddress }).ToArray(),
                Writers = topology.histories, Transitions = topology.outcomes, Faults = topology.faults, State = state,
                Boundary = "Actual Kestrel writes/flush/completion; no sleeps used as terminal evidence. No deployed retention/restore certification."
            }, new JsonSerializerOptions { WriteIndented = true }), CancellationToken.None);
        }
    }

    private static async Task<string> CreateDatabaseAsync(PostgreSqlFixture fixture, string prefix, CancellationToken ct)
    {
        var name = prefix + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(fixture.Container.GetConnectionString());
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
        await command.ExecuteNonQueryAsync(ct);
        return new NpgsqlConnectionStringBuilder(fixture.Container.GetConnectionString())
        { Database = name, Host = "127.0.0.1", Pooling = false }.ConnectionString;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var host in hosts)
            await host.DisposeAsync();
        await link.DisposeAsync();
    }
}

internal sealed record DriverLifecycleSnapshot(DriverAuthorizationGrant[] Grants, DriverLifecycleOperation[] Operations,
    DriverCopyCleanup[] Cleanup, DriverPublicationAdmission[] Admissions, DriverTrackedCopy[] Copies);
