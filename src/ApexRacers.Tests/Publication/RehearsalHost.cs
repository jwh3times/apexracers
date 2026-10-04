using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace ApexRacers.Tests.Publication;

internal sealed class WriterProbe
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource> phases = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource> holds = new();
    private readonly ConcurrentQueue<WriterEvent> events = new();
    private int ended;

    public int BackendPid { get; set; }
    public bool Ended => Volatile.Read(ref ended) == 1;
    public void End() => Volatile.Write(ref ended, 1);

    public WriterEvent[] Events => events.ToArray();

    public void Reach(string phase)
    {
        events.Enqueue(new WriterEvent(phase, DateTimeOffset.UtcNow));
        Signal(phase).TrySetResult();
    }

    public async Task ReachAsync(string phase, CancellationToken ct)
    {
        Reach(phase);
        if (holds.TryGetValue(phase, out var hold))
            await hold.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
    }

    public void Hold(string phase) => holds.TryAdd(phase,
        new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

    public bool Release(string phase) => holds.TryGetValue(phase, out var hold) && hold.TrySetResult();

    public Task WaitAsync(string phase, CancellationToken ct) =>
        Signal(phase).Task.WaitAsync(TimeSpan.FromSeconds(30), ct);

    private TaskCompletionSource Signal(string phase) =>
        phases.GetOrAdd(phase, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
}

internal sealed record WriterEvent(string Phase, DateTimeOffset At);
internal sealed record WriterObservation(string Id, int BackendPid, bool Ended, WriterEvent[] Events);

/// <summary>Only the test executable can start this loopback host. It never loads product startup.</summary>
internal static class RehearsalHost
{
    public static async Task RunAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("PUBLICATION_REHEARSAL_DATABASE")
            ?? throw new InvalidOperationException("Synthetic rehearsal database is required.");
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database;
        if (database is null || !database.StartsWith("apexracers_rehearsal_", StringComparison.Ordinal))
            throw new InvalidOperationException("Only an isolated rehearsal database is permitted.");
        var incarnation = Guid.Parse(Environment.GetEnvironmentVariable("PUBLICATION_REHEARSAL_INCARNATION")!);
        var store = new RehearsalStore(connectionString);
        var writers = new ConcurrentDictionary<string, WriterProbe>();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        await using var app = builder.Build();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        app.MapGet("/publish/{id}", async (string id, HttpContext context) =>
        {
            var probe = writers.GetOrAdd(id, _ => new WriterProbe());
            await probe.ReachAsync("before-admission", context.RequestAborted);
            await using var connection = await store.OpenAsync(context.RequestAborted);
            probe.BackendPid = connection.ProcessID;
            if (!await RehearsalStore.AdmitAsync(connection, id, incarnation, context.RequestAborted))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            try
            {
                await probe.ReachAsync("admitted", context.RequestAborted);
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync("synthetic-first\n", context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
                await probe.ReachAsync("first-written", context.RequestAborted);
                await context.Response.WriteAsync("synthetic-last\n", context.RequestAborted);
                await context.Response.CompleteAsync();
                probe.Reach("last-written");
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                context.Abort();
                probe.Reach("client-aborted");
            }
            catch
            {
                // Abort unexpected executor failure too; let it fail the scenario instead of
                // leaving a live response writer behind a terminal checkpoint.
                context.Abort();
                probe.Reach("writer-failed");
                throw;
            }
            finally
            {
                // This executor has no detached write tasks. It has completed/aborted transport
                // and will make no further application writes before recording the checkpoint.
                probe.End();
                await probe.ReachAsync("writer-ended", lifetime.Token);
                try
                {
                    await RehearsalStore.CheckpointAsync(connection, id, incarnation, lifetime.Token);
                    probe.Reach("checkpointed");
                }
                catch (NpgsqlException)
                {
                    // Losing coordination must not delete a durable admission or infer its completion.
                    probe.Reach("checkpoint-unknown");
                }
            }
        });
        app.MapPost("/withdraw", async () =>
        {
            var result = await store.WithdrawAsync(lifetime.Token);
            return Results.Json(result, statusCode: result.Completed ? 200 : 202);
        });
        app.MapGet("/control/wait/{id}/{phase}", async (string id, string phase, HttpContext context) =>
        {
            await writers.GetOrAdd(id, _ => new WriterProbe()).WaitAsync(phase, context.RequestAborted);
            return Results.Ok();
        });
        app.MapPost("/control/hold/{id}/{phase}", (string id, string phase) =>
        {
            writers.GetOrAdd(id, _ => new WriterProbe()).Hold(phase);
            return Results.Ok();
        });
        app.MapPost("/control/release/{id}/{phase}", (string id, string phase) =>
            writers.TryGetValue(id, out var probe) && probe.Release(phase)
                ? Results.Ok() : Results.BadRequest());
        app.MapGet("/control/observations", () =>
            writers.Select(pair => new WriterObservation(pair.Key, pair.Value.BackendPid,
                pair.Value.Ended, pair.Value.Events)).ToArray());
        app.MapPost("/control/retry-checkpoint/{id}", async (string id) =>
        {
            if (!writers.TryGetValue(id, out var probe) || !probe.Ended)
                return Results.Conflict();
            try
            {
                await using var connection = await store.OpenAsync(lifetime.Token);
                await RehearsalStore.CheckpointAsync(connection, id, incarnation, lifetime.Token);
                probe.Reach("checkpoint-recovered");
                return Results.Ok();
            }
            catch (NpgsqlException)
            {
                probe.Reach("checkpoint-retry-unavailable");
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
        });
        await app.StartAsync(lifetime.Token);
        var address = app.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses.Single();
        Console.WriteLine($"REHEARSAL_READY {address}");
        await app.WaitForShutdownAsync(lifetime.Token);
    }
}
