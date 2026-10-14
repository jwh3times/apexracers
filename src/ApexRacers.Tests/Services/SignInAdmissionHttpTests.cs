using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using ApexRacers.Api.Dtos;
using ApexRacers.Api.Services;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace ApexRacers.Tests.Services;

/// <summary>
/// Actual ordinary Program/controller/limiter over disposable loopback PostgreSQL. A fixture-only
/// trigger holds the fifth paired admission; no production registration or limiter is replaced.
/// Account failure accounting witnesses checked wrong passwords, rather than claimed attempts.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class SignInAdmissionHttpTests(PostgreSqlFixture postgres)
{
    private const string Email = "http-admission@example.com";
    private const string Password = "SyntheticPass1234!";
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OwnedDeviceConcurrentFallbackCannotAddPasswordChecksBelowDefaultTransportLimit()
    {
        await using var host = await StartAsync();
        var cookie = await host.LoginCookieAsync(Email, Ct);
        for (var i = 0; i < 4; i++) await RefusalAsync(host, cookie);
        await using var control = new NpgsqlConnection(host.Connection);
        await control.OpenAsync(Ct);
        await ExecuteAsync(control, "SELECT pg_advisory_lock(453510)", Ct);
        await ExecuteAsync(control, """
            CREATE FUNCTION identity.hold_fifth_device_admission() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF EXISTS (SELECT 1 FROM identity."KnownDevices"
                           WHERE "UserId" = NEW."UserId" AND "FailureCount" = 5) THEN
                    PERFORM pg_advisory_xact_lock(453510);
                END IF;
                RETURN NEW;
            END $$;
            CREATE TRIGGER hold_fifth_device_admission BEFORE INSERT ON identity."SignInAddressFailures"
            FOR EACH ROW EXECUTE FUNCTION identity.hold_fifth_device_admission();
            """, Ct);
        var a = RefusalAsync(host, cookie);
        Task? b = null;
        try
        {
            await WaitAsync(async () => await WaitingAsync(control, "advisory"), Ct);
            b = RefusalAsync(host, cookie);
            // The repaired path waits for A's User row. The vulnerable path instead completes B's
            // address fallback while A's address claim is held; both are observed without a delay
            // masquerading as evidence of admission serialization.
            await WaitAsync(async () => b.IsCompleted || await WaitingAsync(control, "transactionid", "FOR UPDATE"), Ct);
        }
        finally
        {
            await ExecuteAsync(control, "SELECT pg_advisory_unlock(453510)", CancellationToken.None);
        }
        await a;
        Assert.NotNull(b);
        await b;
        await AssertCheckedFailuresAsync(host, 5);
        await AssertNoticeAsync(host);
        // One successful cookie mint + six refusals consumed seven default auth permits.
        for (var request = 8; request <= 10; request++) await RefusalAsync(host, cookie);
        using var capped = await host.PostAsync(Email, "WrongPassword", cookie, Ct);
        Assert.Equal(HttpStatusCode.TooManyRequests, capped.StatusCode);
        await AssertCheckedFailuresAsync(host, 5);
        await AssertNoticeAsync(host);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("arbitrary")]
    [InlineData("unowned")]
    [InlineData("owned-sequential")]
    public async Task CookieControlsRetainDefaultFiveChecksAndEleventhRequestCap(string mode)
    {
        await using var host = await StartAsync();
        string? cookie = mode switch
        {
            "unowned" => await host.LoginCookieAsync("other-http-admission@example.com", Ct),
            "owned-sequential" => await host.LoginCookieAsync(Email, Ct),
            "arbitrary" => "synthetic-unrecognised-device",
            _ => null,
        };
        for (var i = 0; i < 6; i++) await RefusalAsync(host, cookie);
        await AssertCheckedFailuresAsync(host, 5);
        await AssertNoticeAsync(host);
        var consumed = mode is "unowned" or "owned-sequential" ? 7 : 6;
        for (var i = consumed; i < 10; i++) await RefusalAsync(host, cookie);
        using var capped = await host.PostAsync(Email, "WrongPassword", cookie, Ct);
        Assert.Equal(HttpStatusCode.TooManyRequests, capped.StatusCode);
        await AssertCheckedFailuresAsync(host, 5);
        await AssertNoticeAsync(host);
    }

    private async Task RefusalAsync(ApiHost host, string? cookie)
    {
        using var response = await host.PostAsync(Email, "WrongPassword", cookie, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal("Invalid email or password.", body.RootElement.GetProperty("detail").GetString());
        Assert.Equal(401, body.RootElement.GetProperty("status").GetInt32());
        Assert.False(body.RootElement.TryGetProperty("token", out _));
        Assert.False(body.RootElement.TryGetProperty("refreshToken", out _));
    }

    private async Task AssertCheckedFailuresAsync(ApiHost host, int count)
    {
        await using var db = new AppDbContext(host.Options);
        Assert.Equal(count, (await db.SignInAccountFailures.SingleAsync(Ct)).FailureCount);
    }

    private async Task AssertNoticeAsync(ApiHost host)
    {
        await using var db = new AppDbContext(host.Options);
        Assert.NotNull((await db.SignInAccountFailures.SingleAsync(Ct)).NoticeSentAt);
        await WaitAsync(() => Task.FromResult(Directory.GetFiles(host.MailDrop, "*.json").Length == 1), Ct);
        Assert.Single(Directory.GetFiles(host.MailDrop, "*.json"));
    }

    private async Task<bool> WaitingAsync(NpgsqlConnection control, string wait, string? query = null)
    {
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (SELECT 1 FROM pg_stat_activity
            WHERE application_name = 'signin-http-synthetic' AND wait_event = @wait
            AND (@query::text IS NULL OR query LIKE '%' || @query || '%'))
            """, control);
        command.Parameters.AddWithValue("wait", wait);
        command.Parameters.AddWithValue("query", (object?)query ?? DBNull.Value);
        return (bool)(await command.ExecuteScalarAsync(Ct))!;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task WaitAsync(Func<Task<bool>> predicate, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        while (!await predicate()) await Task.Delay(20, deadline.Token);
    }

    private async Task<ApiHost> StartAsync()
    {
        var initial = await postgres.CreateOptionsAsync(Ct);
        await using var empty = new AppDbContext(initial);
        var connection = new NpgsqlConnectionStringBuilder(empty.Database.GetConnectionString())
        {
            Host = "127.0.0.1", ApplicationName = "signin-http-synthetic", Pooling = false,
        }.ConnectionString;
        await empty.Database.EnsureDeletedAsync(Ct);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options;
        var host = await ApiHost.StartAsync(connection, options, Ct);
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddScoped(_ => new AppDbContext(options));
            services.AddIdentityCore<ApplicationUser>().AddEntityFrameworkStores<AppDbContext>();
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            foreach (var email in new[] { Email, "other-http-admission@example.com" })
                Assert.True((await users.CreateAsync(new ApplicationUser
                {
                    Id = Guid.NewGuid(), UserName = email, Email = email, EmailConfirmed = true,
                    DisplayName = "Synthetic HTTP User",
                }, Password)).Succeeded);
            return host;
        }
        catch { await host.DisposeAsync(); throw; }
    }

    private sealed class ApiHost(Process child, HttpClient client, string connection,
        DbContextOptions<AppDbContext> options, string mailDrop, Task<string> output, Task<string> errors) : IAsyncDisposable
    {
        private HttpClient Client => client;
        public string Connection => connection;
        public DbContextOptions<AppDbContext> Options => options;
        public string MailDrop => mailDrop;
        public async Task<HttpResponseMessage> PostAsync(string email, string password, string? cookie, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = JsonContent.Create(new LoginRequest(email, password)),
            };
            if (cookie is not null) request.Headers.Add("Cookie", KnownDeviceCookie.DevelopmentName + "=" + cookie);
            return await client.SendAsync(request, ct);
        }
        public async Task<string> LoginCookieAsync(string email, CancellationToken ct)
        {
            using var response = await PostAsync(email, Password, null, ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return Assert.Single(response.Headers.GetValues("Set-Cookie")).Split(';')[0].Split('=', 2)[1];
        }
        public static async Task<ApiHost> StartAsync(string connection, DbContextOptions<AppDbContext> options, CancellationToken ct)
        {
            var target = new NpgsqlConnectionStringBuilder(connection);
            if (target.Host != "127.0.0.1" || target.Database?.StartsWith("apexracers_test_", StringComparison.Ordinal) != true)
                throw new InvalidOperationException("Only a disposable loopback PostgreSQL fixture may host admission tests.");
            var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root.Parent is not null && !File.Exists(Path.Combine(root.FullName, "global.json"))) root = root.Parent;
            var drop = Path.Combine(Path.GetTempPath(), "apexracers-signin-http-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(drop);
            var info = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = root.FullName, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            info.ArgumentList.Add(typeof(JwtSettings).Assembly.Location);
            info.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
            info.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            info.Environment["DATABASE_CONNECTION_STRING"] = connection;
            info.Environment["JWT_SIGNING_KEY"] = "synthetic-http-only-signing-key-at-least-32-bytes";
            info.Environment["JWT_ISSUER"] = "SyntheticAdmission";
            info.Environment["JWT_AUDIENCE"] = "SyntheticAdmission";
            info.Environment["DEV_MAIL_DROP_PATH"] = drop;
            foreach (var name in new[] { "AZURE_KEY_VAULT_URL", "ACS_CONNECTION_STRING", "ADMIN_SEED_EMAILS",
                "ASPNETCORE_FORWARDEDHEADERS_ENABLED", "AUTH_RATE_LIMIT_PERMIT_PER_MINUTE",
                "GLOBAL_RATE_LIMIT_PERMIT_PER_MINUTE", "SIGNIN_MAX_FAILURES_PER_ADDRESS",
                "SIGNIN_MAX_FAILURES_PER_ADDRESS_UNDER_ATTACK", "SIGNIN_ACCOUNT_HIGH_WATER_FAILURES",
                "SIGNIN_ADDRESS_WINDOW_MINUTES", "SIGNIN_ACCOUNT_WINDOW_MINUTES", "SIGNIN_NOTICE_INTERVAL_MINUTES",
                "IRACING_USERNAME", "IRACING_PASSWORD", "IRACING_CLIENT_ID", "IRACING_CLIENT_SECRET" })
                info.Environment.Remove(name);
            var child = Process.Start(info)!;
            var host = new ApiHost(child, new HttpClient(new SocketsHttpHandler { UseProxy = false, UseCookies = false })
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}"),
            }, connection, options, drop, child.StandardOutput.ReadToEndAsync(), child.StandardError.ReadToEndAsync());
            try
            {
                await WaitAsync(async () =>
                {
                    if (child.HasExited) throw new InvalidOperationException("Synthetic ordinary API failed startup (exit " + child.ExitCode + ").");
                    try { using var ready = await host.Client.GetAsync("/ready", ct); return ready.IsSuccessStatusCode; }
                    catch (HttpRequestException) { return false; }
                }, ct);
                return host;
            }
            catch { await host.DisposeAsync(); throw; }
        }
        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync(CancellationToken.None);
            await output;
            await errors;
            child.Dispose();
            Directory.Delete(mailDrop, recursive: true);
        }
    }
}
