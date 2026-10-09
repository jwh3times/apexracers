using System.Diagnostics;
using System.Formats.Tar;
using ApexRacers.Api.Services;
using ApexRacers.Tests.References;

namespace ApexRacers.Tests.Restore;

/// <summary>Actual pinned pre-lifecycle API, built from Git; no rewritten controller or fake response.</summary>
internal sealed class LegacyApiProcess(Process child, HttpClient client) : IAsyncDisposable
{
    public const string LegacyCommit = "abf8050f3d12b0b667c3501d3246416a0c9feca8";
    private static readonly Lazy<Task<string>> Binary = new(BuildAsync);
    private static string? buildDirectory;
    public HttpClient Client { get; } = client;
    public int ProcessId => child.Id;
    public static string RepositoryRoot
    {
        get
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root.Parent is not null && !File.Exists(Path.Combine(root.FullName, "global.json"))) root = root.Parent;
            return root.FullName;
        }
    }
    private static async Task<string> BuildAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "apexracers_restore_legacy_" + Guid.NewGuid().ToString("N"));
        buildDirectory = root;
        Directory.CreateDirectory(root);
        var archive = Path.Combine(root, "source.tar");
        await CommandAsync("git", ["archive", "--format=tar", "--output", archive, LegacyCommit], RepositoryRoot);
        TarFile.ExtractToDirectory(archive, root, overwriteFiles: false);
        var project = Path.Combine(root, "src", "ApexRacers.Api", "ApexRacers.Api.csproj");
        await CommandAsync("dotnet", ["build", project, "--configuration", "Release", "--verbosity", "quiet"], root);
        return Path.Combine(root, "src", "ApexRacers.Api", "bin", "Release", "net10.0", "ApexRacers.Api.dll");
    }
    public static async Task<string> BinarySha256Async(CancellationToken ct)
    {
        await using var binary = File.OpenRead(await Binary.Value.WaitAsync(ct));
        return Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(binary, ct));
    }
    public static async ValueTask CleanupBuildAsync()
    {
        if (Binary.IsValueCreated)
        {
            try { await Binary.Value; } catch { /* A failed build still owns its temporary directory. */ }
            if (buildDirectory is not null && Directory.Exists(buildDirectory)) Directory.Delete(buildDirectory, recursive: true);
        }
    }
    private static async Task CommandAsync(string executable, string[] args, string directory)
    {
        var info = new ProcessStartInfo(executable) { WorkingDirectory = directory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(3)); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); throw; }
        var log = await output + await errors;
        if (process.ExitCode != 0) throw new InvalidOperationException("Pinned legacy build failed: " + log);
    }
    public static async Task<LegacyApiProcess> StartAsync(string database, CancellationToken ct, bool current = false)
    {
        var target = new Npgsql.NpgsqlConnectionStringBuilder(database);
        if (target.Host is not ("127.0.0.1" or "localhost") || target.Database?.StartsWith("apexracers_restore_legacy_", StringComparison.Ordinal) != true)
            throw new InvalidOperationException("Only a disposable loopback restore database may host the pinned legacy binary.");
        // Use the same compiled module path as the test host. Loading an identical second copy
        // from the API output corrupts the collector's combined module counters.
        var binary = current ? typeof(JwtSettings).Assembly.Location : await Binary.Value.WaitAsync(ct);
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var info = new ProcessStartInfo("dotnet") { WorkingDirectory = RepositoryRoot, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add(binary);
        info.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        info.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        info.Environment["DATABASE_CONNECTION_STRING"] = database;
        foreach (var key in new[] { "AZURE_KEY_VAULT_URL", "IRACING_USERNAME", "IRACING_PASSWORD", "IRACING_CLIENT_ID", "IRACING_CLIENT_SECRET", "ACS_CONNECTION_STRING", "DEV_MAIL_DROP_PATH", "ADMIN_SEED_EMAILS" }) info.Environment[key] = "";
        var jwt = JwtSettings.FromConfiguration(ReferenceActors.Configuration);
        info.Environment["JWT_SIGNING_KEY"] = jwt.SigningKey; info.Environment["JWT_ISSUER"] = jwt.Issuer; info.Environment["JWT_AUDIENCE"] = jwt.Audience;
        var child = Process.Start(info)!;
        _ = child.StandardOutput.ReadToEndAsync(ct); _ = child.StandardError.ReadToEndAsync(ct);
        var host = new LegacyApiProcess(child, new(new SocketsHttpHandler { UseProxy = false }) { BaseAddress = new($"http://127.0.0.1:{port}") });
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(30));
            while (true)
            {
                if (child.HasExited) throw new InvalidOperationException("Synthetic API restore host failed to start.");
                try { using var ready = await host.Client.GetAsync("/ready", deadline.Token); if (ready.IsSuccessStatusCode) return host; }
                catch (HttpRequestException) { }
                await Task.Delay(100, deadline.Token);
            }
        }
        catch { await host.DisposeAsync(); throw; }
    }
    public async ValueTask DisposeAsync()
    {
        Client.Dispose(); if (!child.HasExited) child.Kill(entireProcessTree: true);
        await child.WaitForExitAsync(CancellationToken.None); child.Dispose();
    }
}
