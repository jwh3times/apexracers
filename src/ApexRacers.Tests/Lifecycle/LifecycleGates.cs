using System.Collections.Concurrent;

namespace ApexRacers.Tests.Lifecycle;

/// <summary>HTTP-controlled barriers; deadlines only bound failed tests, never establish terminality.</summary>
internal sealed class LifecycleGates
{
    private readonly ConcurrentDictionary<(string Id, string Phase), TaskCompletionSource> reached = new();
    private readonly ConcurrentDictionary<(string Id, string Phase), TaskCompletionSource> held = new();
    private readonly ConcurrentQueue<LifecycleEvent> events = new();
    private readonly ConcurrentDictionary<string, byte> ended = new();
    private readonly ConcurrentDictionary<string, byte> faults = new();

    public LifecycleEvent[] Events => events.ToArray();
    public bool HasEnded(string id) => ended.ContainsKey(id);
    public void End(string id) => ended.TryAdd(id, 0);
    public void SetFault(string boundary) => faults.TryAdd(boundary, 0);
    public void ClearFault(string boundary) => faults.TryRemove(boundary, out _);
    public bool IsFaulted(string boundary) => faults.ContainsKey(boundary);

    public void Hold(string id, string phase) => held.TryAdd((id, phase), NewSignal());
    public bool Release(string id, string phase) =>
        held.TryGetValue((id, phase), out var signal) && signal.TrySetResult();

    public async Task ReachAsync(string id, string phase, CancellationToken ct)
    {
        events.Enqueue(new LifecycleEvent(id, phase, DateTimeOffset.UtcNow));
        reached.GetOrAdd((id, phase), _ => NewSignal()).TrySetResult();
        if (held.TryGetValue((id, phase), out var signal))
            await signal.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
    }

    public Task WaitAsync(string id, string phase, CancellationToken ct) =>
        reached.GetOrAdd((id, phase), _ => NewSignal()).Task.WaitAsync(TimeSpan.FromSeconds(30), ct);

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed record LifecycleEvent(string Id, string Phase, DateTimeOffset At);
