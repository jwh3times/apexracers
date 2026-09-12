using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Threading.RateLimiting;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Azure.Communication.Email;
using ApexRacers.Api.Middleware;
using ApexRacers.Core;
using ApexRacers.Api.Services;
using ApexRacers.Api.Services.Email;
using ApexRacers.Data;
using Aydsko.iRacingData;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Logging providers are left at the framework defaults on purpose: in Azure the App
// Service Application Insights codeless agent injects its own ILogger provider, and
// calling ClearProviders() here would remove it and suppress trace telemetry. App
// Insights is the structured-telemetry pipeline (requests/dependencies/exceptions/traces);
// RequestLoggingMiddleware adds a per-request log that flows to it and to the console.
var keyVaultUrl = builder.Configuration["AZURE_KEY_VAULT_URL"];
if (!string.IsNullOrEmpty(keyVaultUrl))
{
    builder.Configuration.AddAzureKeyVault(
        new Uri(keyVaultUrl),
        new DefaultAzureCredential(),
        new HyphenToUnderscoreSecretManager());
}

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "ApexRacers API";
        document.Info.Version = "v1";
        return Task.CompletedTask;
    });
});

var connectionString =
    builder.Configuration["DATABASE_CONNECTION_STRING"]
    ?? throw new InvalidOperationException("DATABASE_CONNECTION_STRING is not set.");

// One binding for both sides of the token contract: this configures validation below, and the
// same instance is injected into AuthService, which issues them.
var jwt = JwtSettings.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(jwt);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString, o => o.MigrationsHistoryTable("__EFMigrationsHistory", "iracing")));

// Liveness (/healthz) runs no checks; readiness (/ready) verifies the DB is reachable.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database");

// ── iRacing Data API — on-demand per-user member fetches ─────────────────────
// Unlike the ingestion worker (which requires these), the API registers the client
// only when all four credentials are present, so local dev / CI without iRacing
// creds still boots. Services that need it check for the client's presence (see
// CachedIRacingClient) and surface a 503 when it isn't configured.
var irUsername = builder.Configuration["IRACING_USERNAME"];
var irPassword = builder.Configuration["IRACING_PASSWORD"];
var irClientId = builder.Configuration["IRACING_CLIENT_ID"];
var irClientSecret = builder.Configuration["IRACING_CLIENT_SECRET"];
if (!string.IsNullOrEmpty(irUsername) && !string.IsNullOrEmpty(irPassword)
    && !string.IsNullOrEmpty(irClientId) && !string.IsNullOrEmpty(irClientSecret))
{
    builder.Services.AddIRacingDataApi(options =>
        options.UsePasswordLimitedOAuth(
            userName: irUsername,
            password: irPassword,
            clientId: irClientId,
            clientSecret: irClientSecret,
            passwordIsEncoded: false,
            clientSecretIsEncoded: false));
}

builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 8;
    options.Password.RequireNonAlphanumeric = false;
    options.User.RequireUniqueEmail = true;

    // Identity's own lockout is deliberately OFF, and must stay off. It counts failures per
    // account regardless of who produced them, which is exactly the denial of service issue #300
    // removed: five requests from a stranger locked a Driver out of their own account for fifteen
    // minutes, repeatable indefinitely. Brute-force protection now lives in SignInThrottleStore,
    // which counts per (account, source address) instead — see ApexRacers.Core.SignInThrottle.
    //
    // AccessFailedCount and LockoutEnd still exist on the user because they are Identity's columns;
    // nothing reads them. Do not reintroduce AccessFailedAsync/IsLockedOutAsync on the sign-in path.
    options.Lockout.AllowedForNewUsers = false;
})
.AddRoles<IdentityRole<Guid>>()
.AddEntityFrameworkStores<AppDbContext>()
// Token providers back password-reset tokens (UserManager.GeneratePasswordResetTokenAsync).
.AddDefaultTokenProviders();

// Per-IP fixed-window rate limit on the auth endpoints — a second, transport-level
// layer of brute-force protection in front of the per-account lockout above.
// Config-driven so CI/E2E (a single-IP serial Playwright suite) can raise the ceiling;
// the production default stays 10.
var authPermitLimit =
    int.TryParse(builder.Configuration["AUTH_RATE_LIMIT_PERMIT_PER_MINUTE"], out var apl) && apl > 0
        ? apl
        : 10;
// Config-driven so CI/E2E (a single-IP serial Playwright suite) can raise the ceiling;
// the production default stays 300.
var globalPermitLimit =
    int.TryParse(builder.Configuration["GLOBAL_RATE_LIMIT_PERMIT_PER_MINUTE"], out var gpl) && gpl > 0
        ? gpl
        : 300;
// Driver search is the one iRacing-backed route whose input is free text, so distinct terms —
// each a distinct cache key and a distinct upstream fetch — are unbounded even after the length
// cap. Bounding the *term* stops the cache bypass; only a limiter bounds how much of the shared
// iRacing service-account quota one caller can spend (GHSA-jv96-89xc-98h2). Partitioned per user
// rather than per IP because the route is authenticated and an IP can carry many users.
var searchPermitLimit =
    int.TryParse(builder.Configuration["SEARCH_RATE_LIMIT_PERMIT_PER_MINUTE"], out var spl) && spl > 0
        ? spl
        : 30;
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Safety-net global cap per client IP: generous enough that a real user never
    // hits it (a page load fires <10 API calls), but bounds scripted abuse on the
    // otherwise-unthrottled endpoints. Health endpoints opt out via DisableRateLimiting().
    // Config-driven via GLOBAL_RATE_LIMIT_PERMIT_PER_MINUTE (default 300); CI/E2E raises it.
    // NOTE: behind a reverse proxy, RemoteIpAddress is the real client only because the
    // forwarded-headers middleware rewrote it — see ForwardedHeadersPolicy for the trust rules
    // and for why only the rightmost X-Forwarded-For entry is believed.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = globalPermitLimit,
                Window      = TimeSpan.FromMinutes(1),
                QueueLimit  = 0,
            }));

    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authPermitLimit,
                Window      = TimeSpan.FromMinutes(1),
                QueueLimit  = 0,
            }));

    // Per-*user* window for driver search. The subject claim is the partition; an unauthenticated
    // caller cannot reach the route (it is [Authorize]) but the partition still has to be total,
    // so it falls back to the IP. 30/min is far above type-ahead use — the frontend debounces —
    // and far below what it takes to walk the name space. Config-driven like the two above.
    options.AddPolicy("iracing-search", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? httpContext.Connection.RemoteIpAddress?.ToString()
                ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = searchPermitLimit,
                Window      = TimeSpan.FromMinutes(1),
                QueueLimit  = 0,
            }));
});

// The forwarded-header trust decision, in code and reviewable rather than implied by an app
// setting (GHSA-fq5w-frqr-6px2). Configuring the options is safe regardless of who registers the
// middleware — see ForwardedHeadersPolicy for why registering it ourselves is not.
builder.Services.Configure<ForwardedHeadersOptions>(ForwardedHeadersPolicy.Configure);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = jwt.ValidationParameters();
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly",     policy => policy.RequireClaim("role", "Admin"));
    options.AddPolicy("AlphaOrAbove",  policy => policy.RequireClaim("role", "Alpha", "Admin"));
    options.AddPolicy("BetaOrAbove",   policy => policy.RequireClaim("role", "Beta", "Alpha", "Admin"));
});

// Constructed by hand because IDataClient is registered only when all four credentials are
// present (above); GetService returns null otherwise, which is exactly what the client's
// nullable parameter means. Container auto-wiring would fail to resolve it instead.
builder.Services.AddScoped(sp =>
    new CachedIRacingClient(sp.GetRequiredService<AppDbContext>(), sp.GetService<IDataClient>()));
builder.Services.AddScoped<FeatureFlagEligibility>();
builder.Services.AddScoped<SubjectDriverContext>();
builder.Services.AddScoped<DriverStatsService>();
builder.Services.AddScoped<AchievementsService>();
builder.Services.AddScoped<RaceHistoryService>();
builder.Services.AddScoped<SubsessionDetailService>();
builder.Services.AddScoped<LapDataService>();
builder.Services.AddScoped<ScheduleService>();
builder.Services.AddScoped<WorldRecordService>();
builder.Services.AddScoped<LeaderboardService>();
builder.Services.AddScoped<StandingsService>();
builder.Services.AddHttpClient<IChunkDownloader, HttpChunkDownloader>();
builder.Services.AddScoped<RaceGuideService>();
builder.Services.AddScoped<RivalService>();
builder.Services.AddScoped<RivalComparisonService>();
builder.Services.AddScoped<CarCatalogService>();
builder.Services.AddScoped<TrackCatalogService>();
builder.Services.AddHostedService<ExternalDataCacheCleanupService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddScoped<SeriesService>();
builder.Services.AddScoped<WeekCarStatsService>();
builder.Services.AddScoped<PercentileCalculationService>();
builder.Services.AddScoped<CarRecommendationService>();
builder.Services.AddScoped<StrategyService>();
builder.Services.AddScoped<UserAnalyticsService>();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddScoped<RefreshTokenStore>();

// Sign-in throttling (issue #300). Thresholds are config-driven for the same reason the rate limits
// above are: CI and E2E drive sign-in far harder than a person does, from one address.
builder.Services.AddSingleton(_ => new SignInThrottleOptions(
    PerAddressMaxFailures: builder.Configuration.GetValue(
        "SIGNIN_MAX_FAILURES_PER_ADDRESS", SignInThrottle.DefaultPerAddressMaxFailures),
    TightenedPerAddressMaxFailures: builder.Configuration.GetValue(
        "SIGNIN_MAX_FAILURES_PER_ADDRESS_UNDER_ATTACK", SignInThrottle.TightenedPerAddressMaxFailures),
    AccountHighWaterFailures: builder.Configuration.GetValue(
        "SIGNIN_ACCOUNT_HIGH_WATER_FAILURES", SignInThrottle.DefaultAccountHighWaterFailures),
    PerAddressWindow: TimeSpan.FromMinutes(builder.Configuration.GetValue(
        "SIGNIN_ADDRESS_WINDOW_MINUTES", SignInThrottle.DefaultPerAddressWindow.TotalMinutes)),
    AccountWindow: TimeSpan.FromMinutes(builder.Configuration.GetValue(
        "SIGNIN_ACCOUNT_WINDOW_MINUTES", SignInThrottle.DefaultAccountWindow.TotalMinutes)),
    NoticeInterval: TimeSpan.FromMinutes(builder.Configuration.GetValue(
        "SIGNIN_NOTICE_INTERVAL_MINUTES", SignInThrottle.DefaultNoticeInterval.TotalMinutes)))
    // Startup failure, not a warning: a tightened allowance of 0 (or a high-water of 0) would refuse
    // the account owner as readily as a guesser and would do it silently. Same reasoning as the
    // minimum signing-key length above.
    .Validated());
builder.Services.AddScoped<SignInThrottleStore>();
// The sign-in security notice is queued, never sent inside the request: it fires only for
// addresses that have an account, so an inline send would price sign-in differently for real and
// unknown addresses and give the account oracle back by latency (or by a 500 when mail fails).
builder.Services.AddSingleton<OutboundEmailQueue>();
builder.Services.AddSingleton<IOutboundEmailQueue>(sp => sp.GetRequiredService<OutboundEmailQueue>());
builder.Services.AddHostedService<OutboundEmailDispatcher>();
builder.Services.AddHostedService<SignInThrottleCleanupService>();

builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AdminSeedService>();

var acsConnectionString = builder.Configuration["ACS_CONNECTION_STRING"];
var mailDropPath = builder.Configuration["DEV_MAIL_DROP_PATH"];
// EmailDelivery.Select throws when the Development-only file drop is configured anywhere else,
// which fails startup rather than quietly writing live reset links to disk in a deployed
// environment. See Services/Email/FileDropEmailSender.cs for why the drop exists at all.
switch (EmailDelivery.Select(mailDropPath, acsConnectionString, builder.Environment.IsDevelopment(), builder.Environment.EnvironmentName))
{
    case EmailDeliveryMode.FileDrop:
        builder.Services.AddScoped<IEmailSender>(sp => new FileDropEmailSender(
            mailDropPath!,
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<FileDropEmailSender>>()));
        break;
    case EmailDeliveryMode.Acs:
        builder.Services.AddSingleton(new EmailClient(acsConnectionString!));
        builder.Services.AddScoped<IEmailSender, AcsEmailSender>();
        break;
    default:
        builder.Services.AddScoped<IEmailSender, LoggingEmailSender>();
        break;
}

builder.Services.AddScoped<TelemetryUploadService>();
builder.Services.AddScoped<UploadedLapService>();

// ViteDev CORS is only needed when the Vite dev server (port 5173) calls the API
// directly. In production the React build is served from wwwroot on the same origin.
builder.Services.AddCors(options =>
{
    options.AddPolicy("ViteDev", policy =>
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

// Apply any pending EF Core migrations before accepting traffic. Running this
// here (rather than in a separate pipeline step) keeps deployments self-contained
// and is safe for a single-instance App Service.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

    // Ensure all roles exist
    foreach (var roleName in new[] { "Standard", "Beta", "Alpha", "Admin" })
    {
        if (!await roleManager.RoleExistsAsync(roleName))
            await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
    }

    // Promote confirmed accounts listed in ADMIN_SEED_EMAILS (Key Vault: ADMIN-SEED-EMAILS).
    var adminSeed = scope.ServiceProvider.GetRequiredService<AdminSeedService>();
    await adminSeed.PromoteConfirmedUsersAsync(app.Configuration["ADMIN_SEED_EMAILS"]);

    // Purge refresh tokens that expired more than 30 days ago so the table does not
    // grow without bound (revoked/expired rows are otherwise never deleted).
    var authService = scope.ServiceProvider.GetRequiredService<AuthService>();
    await authService.PurgeExpiredRefreshTokensAsync(TimeSpan.FromDays(30));
}

// Forwarded-header processing is registered by the host, not here - see ForwardedHeadersPolicy for
// why adding app.UseForwardedHeaders() alongside it would consume two entries and hand a caller the
// address the rate limiter partitions on. Log which state we are in, so a dropped app setting shows
// up at startup rather than as an unexplained rate-limit anomaly later.
if (!ForwardedHeadersPolicy.IsEnabledByHost(
        builder.Configuration[ForwardedHeadersPolicy.EnabledVariable]))
{
    app.Logger.LogWarning(
        "{Variable} is not set: X-Forwarded-For and X-Forwarded-Proto are ignored, so per-IP rate "
        + "limiting partitions on the immediate peer. Expected with no reverse proxy in front of "
        + "this instance; behind one, per-IP limits and HSTS will not see real clients.",
        ForwardedHeadersPolicy.EnabledVariable);
}

// Outermost middleware so it times the whole request and observes the final response
// status code (after ExceptionHandlingMiddleware's exception → problem+json mapping).
app.UseMiddleware<RequestLoggingMiddleware>();

// First in the pipeline so it wraps every downstream component and turns any
// unhandled exception into an RFC-7807 problem+json response.
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Before UseStaticFiles so SPA assets get the headers too.
app.UseMiddleware<SecurityHeadersMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().DisableRateLimiting();
    app.MapScalarApiReference((options, context) =>
        {
            var nonce = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            options.WithTitle("ApexRacers API v1")
                .WithOpenApiRoutePattern("/openapi/{documentName}.json")
                .DisableDefaultFonts()
                .DisableAgent()
                .DisableTelemetry()
                .AddHeadContent($"<meta property=\"csp-nonce\" content=\"{nonce}\" />")
                .WithNonce(nonce);
            context.Response.Headers.ContentSecurityPolicy = ContentSecurityPolicy.ForScalar(nonce);
            context.Response.Headers.CacheControl = "no-store";
        })
        .DisableRateLimiting();
    app.UseCors("ViteDev");
}

// Serve the React SPA from wwwroot (populated by the Docker build).
app.UseStaticFiles();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Anonymous probe endpoints, exempt from the global rate limiter so aggressive
// platform probes can't consume a client's budget (or get 429'd themselves).
// App Service's Health check feature points at /healthz (maintainer-only runbook:
// private/ops/azure-deployment-runbook.md, "Verified Runtime State").
app.MapHealthChecks("/healthz", new HealthCheckOptions { Predicate = _ => false })
    .DisableRateLimiting();
app.MapHealthChecks("/ready")
    .DisableRateLimiting();

// Return index.html for any route not matched by a controller so React Router
// can handle client-side navigation (e.g. /series/1/weeks/2).
app.MapFallbackToFile("index.html");

app.Run();

// Key Vault secret names use hyphens (e.g. IRACING-USERNAME); this maps them
// back to the underscore-style keys the rest of the app expects.
class HyphenToUnderscoreSecretManager : KeyVaultSecretManager
{
    public override string GetKey(KeyVaultSecret secret) =>
        secret.Name.Replace('-', '_');
}
