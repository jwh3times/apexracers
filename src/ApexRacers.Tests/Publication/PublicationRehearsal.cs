using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using ApexRacers.Tests.Helpers;
using Npgsql;
using Xunit;

namespace ApexRacers.Tests.Publication;

internal sealed class PublicationRehearsal(
    RehearsalProcess publisher, RehearsalProcess coordinator, RehearsalStore store,
    RehearsalDatabaseLink databaseLink, CancellationToken ct)
{
    private static readonly string artifactDirectory = FindArtifactDirectory();
    private readonly List<WithdrawalObservation> withdrawals = [];
    private readonly List<string> faults = [];
    private readonly List<RehearsalProcess> replacements = [];
    private readonly List<RehearsalProcess> stopped = [];
    private readonly List<HostWriters> writerHistory = [];

    public void PartitionPublisherDatabaseLink()
    {
        databaseLink.Partition();
        faults.Add("publisher database link partitioned: existing connections cut; new connections refused");
    }

    public void RestorePublisherDatabaseLink()
    {
        databaseLink.Restore();
        faults.Add("publisher database link restored; durable checkpoint still required");
    }

    public async Task RestartPublisherAsync()
    {
        await CaptureWritersAsync();
        await publisher.StopAsync(ct);
        stopped.Add(publisher);
        faults.Add($"confirmed OS exit of process {publisher.ProcessId}, incarnation {publisher.Incarnation}; not yet checkpointed");
        publisher = await RehearsalProcess.StartAsync(databaseLink.ConnectionString, ct);
        replacements.Add(publisher);
    }

    public async Task RecordConfirmedOldProcessExitAsync()
    {
        var previous = stopped.Single();
        if (!previous.HasExited)
            throw new InvalidOperationException("Process exit has not been confirmed by the owning supervisor.");
        await store.CheckpointStoppedIncarnationAsync(previous.Incarnation, ct);
        faults.Add($"supervisor checkpoint for confirmed stopped incarnation {previous.Incarnation}");
    }

    private async Task CaptureWritersAsync()
    {
        using var captureDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        foreach (var host in new[] { publisher, coordinator })
        {
            var observations = await host.Client.GetFromJsonAsync<WriterObservation[]>(
                "/control/observations", captureDeadline.Token);
            writerHistory.Add(new HostWriters(host.Incarnation, observations!));
        }
    }

    public async Task ExpireLeaseAsync(string id)
    {
        await store.ExpireLeaseAsync(id, ct);
        faults.Add($"lease expired while {id} remained held in a living HTTP process");
    }

    public Task RetryCheckpointAsync(string id) => ControlAsync($"/control/retry-checkpoint/{id}");

    public async Task TerminateWriterSessionAsync(string id)
    {
        var observations = (await publisher.Client.GetFromJsonAsync<WriterObservation[]>("/control/observations", ct))!;
        var pid = observations.Single(observation => observation.Id == id).BackendPid;
        await store.TerminateSessionAsync(pid, ct);
        faults.Add($"terminated actual PostgreSQL backend {pid} for living HTTP writer {id}");
    }

    public async Task<bool> CanAcquireFormerWriterLockAsync()
    {
        var acquired = await store.CanAcquireSessionLockAsync(ct);
        faults.Add($"former writer session lock acquirable: {acquired}");
        return acquired;
    }

    public Task HoldAsync(string id, string phase, bool onCoordinator = false) =>
        ControlAsync($"/control/hold/{id}/{phase}", onCoordinator);

    public Task ReleaseAsync(string id, string phase, bool onCoordinator = false) =>
        ControlAsync($"/control/release/{id}/{phase}", onCoordinator);

    public async Task<WriterEvent[]> ObserveAsync(string id)
    {
        var observations = (await publisher.Client.GetFromJsonAsync<WriterObservation[]>("/control/observations", ct))!;
        return observations.Single(observation => observation.Id == id).Events;
    }

    public async Task ResetConnectionAfterFirstChunkAsync(string id)
    {
        using var socket = new TcpClient();
        var address = publisher.Client.BaseAddress!;
        await socket.ConnectAsync(address.Host, address.Port, ct);
        var stream = socket.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            $"GET /publish/{id} HTTP/1.1\r\nHost: {address.Authority}\r\nConnection: keep-alive\r\n\r\n"), ct);
        var bytes = new byte[4096];
        var received = new StringBuilder();
        while (!received.ToString().Contains("synthetic-first\n", StringComparison.Ordinal))
        {
            var count = await stream.ReadAsync(bytes, ct);
            if (count == 0)
                throw new InvalidOperationException("Socket closed before the first synthetic chunk.");
            received.Append(Encoding.ASCII.GetString(bytes, 0, count));
            if (received.Length > 16384)
                throw new InvalidOperationException("Unexpected rehearsal response size.");
        }
        // An actual TCP reset, not a substituted RequestAborted token or response stream.
        socket.Client.LingerState = new LingerOption(true, 0);
    }

    private async Task ControlAsync(string uri, bool onCoordinator = false)
    {
        using var result = await (onCoordinator ? coordinator : publisher).Client.PostAsync(uri, null, ct);
        result.EnsureSuccessStatusCode();
    }

    public Task<HttpResponseMessage> PublishAsync(string id, CancellationToken cancellationToken = default,
        bool onCoordinator = false) =>
        (onCoordinator ? coordinator : publisher).Client.GetAsync($"/publish/{id}", HttpCompletionOption.ResponseHeadersRead,
            cancellationToken == default ? ct : cancellationToken);

    public async Task WaitAsync(string id, string phase, bool onCoordinator = false)
    {
        using var result = await (onCoordinator ? coordinator : publisher).Client.GetAsync($"/control/wait/{id}/{phase}", ct);
        result.EnsureSuccessStatusCode();
    }

    public async Task<WithdrawalObservation> WithdrawAsync()
    {
        using var response = await coordinator.Client.PostAsync("/withdraw", null, ct);
        response.EnsureSuccessStatusCode();
        var result = (await response.Content.ReadFromJsonAsync<WithdrawalObservation>(ct))!;
        withdrawals.Add(result);
        return result;
    }

    public static async Task RunAsync(
        PostgreSqlFixture fixture, string scenario, Func<PublicationRehearsal, Task> exercise)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        var ct = deadline.Token;
        var started = DateTimeOffset.UtcNow;
        var databaseName = $"apexracers_rehearsal_{Guid.NewGuid():N}";
        var baseConnection = fixture.Container.GetConnectionString();
        await using (var connection = new NpgsqlConnection(baseConnection))
        {
            await connection.OpenAsync(ct);
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
            await command.ExecuteNonQueryAsync(ct);
        }
        var connectionString = new NpgsqlConnectionStringBuilder(baseConnection)
        {
            Database = databaseName,
            Pooling = false,
        }.ConnectionString;
        var store = new RehearsalStore(connectionString);
        await store.InitializeAsync(ct);
        await using var databaseLink = RehearsalDatabaseLink.Create(connectionString);
        await using var publisher = await RehearsalProcess.StartAsync(databaseLink.ConnectionString, ct);
        await using var coordinator = await RehearsalProcess.StartAsync(connectionString, ct);
        var rehearsal = new PublicationRehearsal(publisher, coordinator, store, databaseLink, ct);
        var passed = false;
        try
        {
            await exercise(rehearsal).WaitAsync(TimeSpan.FromSeconds(75), ct);
            passed = true;
        }
        finally
        {
            try
            {
            try
            {
                await rehearsal.CaptureWritersAsync();
            }
                catch (Exception error) when (!passed && (error is HttpRequestException or OperationCanceledException))
            {
                rehearsal.faults.Add($"final writer observation unavailable: {error.GetType().Name}");
            }
            var directory = artifactDirectory;
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, scenario + ".json"),
                JsonSerializer.Serialize(new
                {
                    SchemaVersion = 1,
                    Scenario = scenario,
                    Passed = passed,
                    Started = started,
                    Finished = DateTimeOffset.UtcNow,
                    Commit = typeof(PublicationRehearsal).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                    Provenance = "synthetic-only",
                    Consent = "controlled synthetic grant; no provider ownership proof",
                    Catalog = "transport feasibility only; no catalog admitted",
                    Runtime = RuntimeInformation.FrameworkDescription,
                    Platform = RuntimeInformation.OSDescription,
                    PostgreSqlImage = "postgres:18.0-alpine",
                    DatabaseTopology = new { PublisherThroughFaultControlledTcpRelay = true, databaseLink.Port, CoordinatorDirect = true, Pooling = false },
                    Hosts = new[] { publisher, coordinator }.Concat(rehearsal.replacements)
                        .Select(host => new { host.ProcessId, host.Incarnation, host.Address }).ToArray(),
                    Withdrawals = rehearsal.withdrawals,
                    Writers = rehearsal.writerHistory,
                    Faults = rehearsal.faults,
                }, new JsonSerializerOptions { WriteIndented = true }), CancellationToken.None);
            }
            finally
            {
                foreach (var replacement in rehearsal.replacements)
                    await replacement.DisposeAsync();
            }
        }
    }

    private static string FindArtifactDirectory()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root.Parent is not null && !File.Exists(Path.Combine(root.FullName, "global.json")))
            root = root.Parent;
        var parent = Environment.GetEnvironmentVariable("PUBLICATION_REHEARSAL_ARTIFACTS")
            ?? Path.Combine(File.Exists(Path.Combine(root.FullName, "global.json")) ? root.FullName : Directory.GetCurrentDirectory(),
                "TestResults", "driver-publication-rehearsal");
        return Path.Combine(parent, $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfff}-{Guid.NewGuid():N}");
    }
}

internal sealed record HostWriters(Guid Incarnation, WriterObservation[] Writers);
