using System.Diagnostics;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ApexRacers.Tests.References;

/// <summary>Ordinary API startup with credential-free --ci --demo data in its own database.</summary>
internal sealed class BrowserDemoHost(Process process) : IAsyncDisposable
{
    public static async Task<BrowserDemoHost> StartAsync(string connection, string webroot, CancellationToken ct)
    {
        var name = "apexracers_browser_demo_" + Guid.NewGuid().ToString("N");
        await using (var c = new NpgsqlConnection(connection))
        {
            await c.OpenAsync(ct);
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", c);
            await command.ExecuteNonQueryAsync(ct);
        }
        var database = new NpgsqlConnectionStringBuilder(connection) { Database = name, Host = "127.0.0.1", Pooling = false }.ConnectionString;
        ProcessStartInfo Command(string project)
        {
            var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            info.ArgumentList.Add(Path.GetFullPath($"src/{project}/bin/Debug/net10.0/{project}.dll"));
            info.Environment["DATABASE_CONNECTION_STRING"] = database;
            info.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:8186";
            info.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            foreach (var key in new[] { "AZURE_KEY_VAULT_URL", "IRACING_USERNAME", "IRACING_PASSWORD", "IRACING_CLIENT_ID", "IRACING_CLIENT_SECRET", "ACS_CONNECTION_STRING", "DEV_MAIL_DROP_PATH", "ADMIN_SEED_EMAILS" }) info.Environment[key] = "";
            foreach (var key in new[] { "JWT_SIGNING_KEY", "JWT_ISSUER", "JWT_AUDIENCE" }) info.Environment[key] = ReferenceActors.Configuration[key]!;
            return info;
        }
        var seed = Command("ApexRacers.Seeder"); seed.ArgumentList.Add("--ci"); seed.ArgumentList.Add("--demo");
        using (var seeder = Process.Start(seed)!)
        {
            var output = seeder.StandardOutput.ReadToEndAsync(ct); var errors = seeder.StandardError.ReadToEndAsync(ct);
            await seeder.WaitForExitAsync(ct); await output; await errors;
            if (seeder.ExitCode != 0) throw new InvalidOperationException("Credential-free Demo seed failed.");
        }
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database).Options))
        {
            db.Users.Add(new ApplicationUser { Id = ReferenceActors.Recipient, DisplayName = "Synthetic Demo User", EmailConfirmed = true });
            var flag = await db.FeatureFlags.SingleAsync(f => f.Key == "iracing-demo", ct);
            flag.IsEnabled = true; flag.MinimumRole = "Standard";
            await db.SaveChangesAsync(ct);
        }
        var commandInfo = Command("ApexRacers.Api"); commandInfo.ArgumentList.Add("--webroot"); commandInfo.ArgumentList.Add(webroot);
        var host = new BrowserDemoHost(Process.Start(commandInfo)!);
        _ = host.process.StandardOutput.ReadToEndAsync(ct); _ = host.process.StandardError.ReadToEndAsync(ct);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(30));
            using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false });
            while (true)
            {
                if (host.process.HasExited) throw new InvalidOperationException("Ordinary synthetic Demo host failed to start.");
                try { using var response = await client.GetAsync("http://127.0.0.1:8186/ready", deadline.Token); if (response.IsSuccessStatusCode) return host; }
                catch (HttpRequestException) { }
                await Task.Delay(100, deadline.Token);
            }
        }
        catch { await host.DisposeAsync(); throw; }
    }
    private Process process { get; } = process;
    public async ValueTask DisposeAsync()
    {
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync(CancellationToken.None); process.Dispose();
    }
}
