using System.Diagnostics;

namespace ApexRacers.Tests.Lifecycle;

internal sealed class LifecycleProcess(Process process, Uri address, Guid incarnation) : IAsyncDisposable
{
    public Guid Incarnation { get; } = incarnation;
    public int ProcessId { get; } = process.Id;
    public bool HasExited => process.HasExited;
    public HttpClient Client { get; } = new(new SocketsHttpHandler { UseProxy = false })
    {
        BaseAddress = address,
        Timeout = TimeSpan.FromSeconds(40),
    };
    // Fault-control requests never share the HTTP/1 pool of a still-running protected writer.
    public HttpClient ControlClient { get; } = new(new SocketsHttpHandler { UseProxy = false })
    {
        BaseAddress = address,
        Timeout = TimeSpan.FromSeconds(40),
    };

    public static async Task<LifecycleProcess> StartAsync(string primary, string journal, CancellationToken ct)
    {
        var incarnation = Guid.NewGuid();
        var info = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add(typeof(LifecycleProcess).Assembly.Location);
        info.ArgumentList.Add("--driver-lifecycle-host");
        info.Environment["DRIVER_LIFECYCLE_DATABASE"] = primary;
        info.Environment["DRIVER_LIFECYCLE_JOURNAL"] = journal;
        info.Environment["DRIVER_LIFECYCLE_INCARNATION"] = incarnation.ToString();
        var process = Process.Start(info) ?? throw new InvalidOperationException("Synthetic lifecycle host did not start.");
        var stderr = process.StandardError.ReadToEndAsync(ct);
        try
        {
            var line = await process.StandardOutput.ReadLineAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(40), ct);
            if (line is null || !line.StartsWith("LIFECYCLE_READY ", StringComparison.Ordinal))
                throw new InvalidOperationException($"Synthetic lifecycle host startup failed: {await stderr.WaitAsync(TimeSpan.FromSeconds(5), ct)}");
            _ = process.StandardOutput.ReadToEndAsync(ct);
            return new LifecycleProcess(process, new Uri(line[16..]), incarnation);
        }
        catch
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
            process.Dispose();
            throw;
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (!process.HasExited)
            process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(10), ct);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        ControlClient.Dispose();
        await StopAsync(CancellationToken.None);
        process.Dispose();
    }
}
