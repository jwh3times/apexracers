using System.Diagnostics;

namespace ApexRacers.Tests.Publication;

internal sealed class RehearsalProcess(Process process, Uri address, Guid incarnation) : IAsyncDisposable
{
    public Guid Incarnation { get; } = incarnation;
    public int ProcessId { get; } = process.Id;
    public Uri Address { get; } = address;
    public bool HasExited => process.HasExited;
    public HttpClient Client { get; } = new(new SocketsHttpHandler { UseProxy = false })
    {
        BaseAddress = address,
        Timeout = TimeSpan.FromSeconds(40),
    };

    public static async Task<RehearsalProcess> StartAsync(string connectionString, CancellationToken ct)
    {
        var incarnation = Guid.NewGuid();
        var info = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add(typeof(RehearsalProcess).Assembly.Location);
        info.ArgumentList.Add("--publication-rehearsal-host");
        info.Environment["PUBLICATION_REHEARSAL_DATABASE"] = connectionString;
        info.Environment["PUBLICATION_REHEARSAL_INCARNATION"] = incarnation.ToString();
        var process = Process.Start(info) ?? throw new InvalidOperationException("Rehearsal host did not start.");
        // Drain diagnostics continuously; never include the database connection string in artifacts.
        var stderr = process.StandardError.ReadToEndAsync(ct);
        try
        {
            var line = await process.StandardOutput.ReadLineAsync(ct).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(40), ct);
            if (line is null || !line.StartsWith("REHEARSAL_READY ", StringComparison.Ordinal))
                throw new InvalidOperationException($"Rehearsal host startup failed: {await stderr.WaitAsync(TimeSpan.FromSeconds(5), ct)}");
            _ = process.StandardOutput.ReadToEndAsync(ct);
            return new RehearsalProcess(process, new Uri(line[16..]), incarnation);
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
        await StopAsync(CancellationToken.None);
        process.Dispose();
    }
}
