using System.Data.Common;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

var mode = args[0];
var output = args[1];
var callOrder = args.Length > 2 ? args[2] : "user-first";
if (callOrder is not ("role-first" or "user-first")) throw new ArgumentException("Unknown diagnostic call order");
var gate = new QueryGate();
PostgreSqlContainer? container = null;
AppDbContext db;
if (mode.StartsWith("sqlite", StringComparison.Ordinal))
    db = DbContextFactory.Create();
else
{
    Resource("testcontainers-session", ResourceReaper.DefaultSessionId.ToString("D"));
    container = new PostgreSqlBuilder("postgres:18.0-alpine").Build();
    await container.StartAsync();
    Resource("container", container.Id, "ready");
    db = new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(container.GetConnectionString())
        .AddInterceptors(gate).Options);
    await db.Database.EnsureCreatedAsync();
}
await using var database = db;
var services = new ServiceCollection();
services.AddLogging();
services.AddScoped(_ => db);
services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole<Guid>>().AddEntityFrameworkStores<AppDbContext>();
await using var provider = services.BuildServiceProvider();
var service = new AdminService(provider.GetRequiredService<UserManager<ApplicationUser>>(), db, new FeatureFlagEligibility(db));
db.FeatureFlags.Add(new() { Key = "diagnostic-standard", Name = "Synthetic diagnostic", MinimumRole = "Standard", IsEnabled = true });
await db.SaveChangesAsync();
var observations = new List<object>();
try
{
    for (var iteration = 0; iteration < 16; iteration++)
    {
        foreach (var method in callOrder == "user-first"
            ? new[] { "GetFlagsForUserAsync", "GetFlagsForRoleAsync" }
            : new[] { "GetFlagsForRoleAsync", "GetFlagsForUserAsync" })
        {
            if (mode == "pg-gated") gate.Arm();
            var queued = mode == "sqlite-strict" ? new QueuedContext(new()) : null;
            var previousContext = SynchronizationContext.Current;
            if (queued is not null) SynchronizationContext.SetSynchronizationContext(queued);
            var task = method == "GetFlagsForRoleAsync"
                ? service.GetFlagsForRoleAsync("Standard")
                : service.GetFlagsForUserAsync(Guid.NewGuid());
            var completedOnReturn = task.IsCompleted;
            var postedBeforePumping = queued?.Posts;
            if (queued is not null)
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
                queued.PumpUntil(task);
            }
            bool? completedWhileQueryPaused = null;
            if (mode == "pg-gated")
            {
                await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
                completedWhileQueryPaused = task.IsCompleted;
                if (completedWhileQueryPaused.Value) throw new InvalidOperationException("Product completed without real query release");
                gate.Release.TrySetResult();
            }
            var result = await task;
            if (result.Count != 1 || result[0].Key != "diagnostic-standard") throw new InvalidOperationException("Actual product flag result changed");
            observations.Add(new { iteration, method, completedOnReturn, postedBeforePumping, totalPosts = queued?.Posts, completedWhileQueryPaused, flags = result.Count });
        }
    }
    var il = new[] { "GetFlagsForRoleAsync", "GetFlagsForUserAsync" }.Select(method =>
    {
        var stateMachine = typeof(AdminService).GetMethod(method)!.GetCustomAttribute<AsyncStateMachineAttribute>()!.StateMachineType;
        var move = stateMachine.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return new { method, stateMachine = stateMachine.FullName, move.MetadataToken,
            ilHex = Convert.ToHexString(move.GetMethodBody()!.GetILAsByteArray()!),
            instructions = Instructions(move) };
    });
    await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { mode, callOrder, observations, il }, new JsonSerializerOptions { WriteIndented = true }));
}
finally
{
    if (container is not null)
    {
        var containerId = container.Id;
        Resource("container", containerId, "disposing");
        await container.DisposeAsync();
        Resource("container", containerId, "disposed");
    }
}

static void Resource(string kind, string id, string? lifecycle = null)
{
    var path = Environment.GetEnvironmentVariable("APEXRACERS_COST_PROBE_DIR");
    if (path is null) return;
    Directory.CreateDirectory(path);
    File.AppendAllText(Path.Combine(path, "diagnostic-phases.jsonl"), JsonSerializer.Serialize(new
    {
        eventType = "resource", processId = Environment.ProcessId, kind, id, lifecycle,
        ticks = Stopwatch.GetTimestamp(), frequency = Stopwatch.Frequency
    }) + Environment.NewLine);
}

static List<object> Instructions(MethodInfo method)
{
    var codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (OpCode)f.GetValue(null)!)
        .GroupBy(op => unchecked((ushort)op.Value)).ToDictionary(g => g.Key, g => g.First());
    var bytes = method.GetMethodBody()!.GetILAsByteArray()!;
    var result = new List<object>();
    for (var offset = 0; offset < bytes.Length;)
    {
        var start = offset;
        ushort code = bytes[offset++];
        if (code == 0xfe) code = (ushort)(0xfe00 | bytes[offset++]);
        var op = codes[code];
        var length = op.OperandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, offset),
            _ => 4
        };
        string? target = null;
        if (op.OperandType == OperandType.InlineMethod)
            target = method.Module.ResolveMethod(BitConverter.ToInt32(bytes, offset))!.ToString();
        result.Add(new { offset = start, opcode = op.Name, operand = Convert.ToHexString(bytes.AsSpan(offset, length)), target });
        offset += length;
    }
    return result;
}

sealed class QueryGate : DbCommandInterceptor
{
    public TaskCompletionSource Reached { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool armed;
    public void Arm()
    {
        Reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        armed = true;
    }
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (armed)
        {
            armed = false;
            Reached.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }
        return result;
    }
}

sealed class QueuedContext(System.Collections.Concurrent.ConcurrentQueue<(SendOrPostCallback Callback, object? State)> callbacks) : SynchronizationContext
{
    public int Posts { get; private set; }
    public override void Post(SendOrPostCallback callback, object? state)
    {
        Posts++;
        callbacks.Enqueue((callback, state));
    }
    public void PumpUntil(Task task)
    {
        var deadline = Stopwatch.GetTimestamp() + 10 * Stopwatch.Frequency;
        while (!task.IsCompleted)
        {
            if (Stopwatch.GetTimestamp() > deadline) throw new TimeoutException("Diagnostic continuation did not complete");
            if (callbacks.TryDequeue(out var next))
            {
                var previous = Current;
                SetSynchronizationContext(this);
                try { next.Callback(next.State); }
                finally { SetSynchronizationContext(previous); }
            }
            else Thread.Sleep(1);
        }
    }
}
