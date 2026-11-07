using System.Collections.Concurrent;
using Azure.Core;
using Azure.Core.Pipeline;

namespace ApexRacers.Tests.IndependentJournal;

/// <summary>Records every request the adapter's clients send, after retries, so tests can assert
/// what the adapter never does (delete, overwrite, append, set metadata).</summary>
internal sealed class RecordingPolicy : HttpPipelinePolicy
{
    public ConcurrentQueue<(string Method, Uri Uri, string? IfNoneMatch, string? BlobType)> Requests { get; } = new();

    public override void Process(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
    {
        Record(message);
        ProcessNext(message, pipeline);
    }

    public override ValueTask ProcessAsync(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
    {
        Record(message);
        return ProcessNextAsync(message, pipeline);
    }

    private void Record(HttpMessage message) => Requests.Enqueue((message.Request.Method.Method, message.Request.Uri.ToUri(),
        message.Request.Headers.TryGetValue("If-None-Match", out var match) ? match : null,
        message.Request.Headers.TryGetValue("x-ms-blob-type", out var type) ? type : null));
}

/// <summary>Holds matching requests until released, so a test can interleave two writers exactly.
/// <see cref="Arrived"/> completes when the first matching request is being held.</summary>
internal sealed class GatePolicy(Func<HttpMessage, bool> matches) : HttpPipelinePolicy
{
    private readonly TaskCompletionSource arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Arrived => arrived.Task;
    public void Release() => released.TrySetResult();

    public override void Process(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline) =>
        ProcessAsync(message, pipeline).AsTask().GetAwaiter().GetResult();

    public override async ValueTask ProcessAsync(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
    {
        if (matches(message))
        {
            arrived.TrySetResult();
            await released.Task;
        }
        await ProcessNextAsync(message, pipeline);
    }
}

/// <summary>Holds the first <paramref name="parties"/> matching requests until all have arrived,
/// then lets them through together, and counts how many matching responses were conflicts.</summary>
internal sealed class BarrierPolicy(Func<HttpMessage, bool> matches, int parties) : HttpPipelinePolicy
{
    private readonly TaskCompletionSource open = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int arrivals;
    private int conflicts;
    public int Arrivals => Volatile.Read(ref arrivals);
    public int Conflicts => Volatile.Read(ref conflicts);

    public override void Process(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline) =>
        ProcessAsync(message, pipeline).AsTask().GetAwaiter().GetResult();

    public override async ValueTask ProcessAsync(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
    {
        if (!matches(message))
        {
            await ProcessNextAsync(message, pipeline);
            return;
        }
        var arrival = Interlocked.Increment(ref arrivals);
        if (arrival == parties) open.TrySetResult();
        if (arrival <= parties) await open.Task;
        await ProcessNextAsync(message, pipeline);
        if (message.Response.Status is 409 or 412) Interlocked.Increment(ref conflicts);
    }
}

/// <summary>Lets a matching write reach storage, then loses its response: the caller cannot tell
/// whether it committed. <see cref="Remaining"/> bounds how many responses are lost.</summary>
internal sealed class LoseResponsePolicy(Func<HttpMessage, bool> matches, int count) : HttpPipelinePolicy
{
    private int remaining = count;
    public int Lost { get; private set; }
    public int Remaining { get => Volatile.Read(ref remaining); set => Volatile.Write(ref remaining, value); }

    public override void Process(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline) =>
        ProcessAsync(message, pipeline).AsTask().GetAwaiter().GetResult();

    public override async ValueTask ProcessAsync(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
    {
        await ProcessNextAsync(message, pipeline);
        if (matches(message) && Interlocked.Decrement(ref remaining) >= 0)
        {
            Lost++;
            throw new IOException("Simulated lost response after the request reached storage.");
        }
    }
}
