using System.Runtime.InteropServices;
using Testcontainers.PostgreSql;

namespace ApexRacers.Tests.References;

/// <summary>Playwright-only bootstrap. Separate primary and independent history databases,
/// two real processes, actual migrations and built SPA; never an API startup mode.</summary>
internal static class BrowserFixture
{
    public static async Task RunAsync()
    {
        var root = Path.GetFullPath("web/dist");
        if (!File.Exists(Path.Combine(root, "index.html"))) throw new InvalidOperationException("Build web/dist before the browser rehearsal.");
        Environment.SetEnvironmentVariable("DRIVER_BROWSER_WEBROOT", root);
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
        using var signal = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => { context.Cancel = true; stop.Cancel(); });
        await using var postgres = new PostgreSqlBuilder("postgres:18.0-alpine").Build();
        await postgres.StartAsync(stop.Token);
        await using var topology = await ReferenceTopology.CreateAsync(postgres.GetConnectionString(), stop.Token, browser: true, port: 8185);
        await using var demo = await BrowserDemoHost.StartAsync(postgres.GetConnectionString(), root, stop.Token);
        Console.WriteLine($"BROWSER_READY {topology.First.Client.BaseAddress}");
        try { await Task.Delay(Timeout.InfiniteTimeSpan, stop.Token); }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }
}
