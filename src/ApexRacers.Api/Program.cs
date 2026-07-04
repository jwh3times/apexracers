using System.Text;
using System.Threading.RateLimiting;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Azure.Communication.Email;
using ApexRacers.Api.Middleware;
using ApexRacers.Api.Services;
using ApexRacers.Api.Services.Email;
using ApexRacers.Data;
using Aydsko.iRacingData;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "ApexRacers API", Version = "v1" });
});

var connectionString =
    builder.Configuration["DATABASE_CONNECTION_STRING"]
    ?? throw new InvalidOperationException("DATABASE_CONNECTION_STRING is not set.");

var jwtKey =
    builder.Configuration["JWT_SIGNING_KEY"]
    ?? throw new InvalidOperationException("JWT_SIGNING_KEY is not set.");

var jwtIssuer = builder.Configuration["JWT_ISSUER"] ?? "ApexRacers.Api";
var jwtAudience = builder.Configuration["JWT_AUDIENCE"] ?? "ApexRacers.Web";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString, o => o.MigrationsHistoryTable("__EFMigrationsHistory", "iracing")));

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

    // Brute-force protection: lock an account for 15 minutes after 5 consecutive
    // failed sign-in attempts. AuthService.LoginAsync drives the counter manually
    // because UserManager.CheckPasswordAsync (unlike SignInManager) does not.
    options.Lockout.AllowedForNewUsers = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
})
.AddRoles<IdentityRole<Guid>>()
.AddEntityFrameworkStores<AppDbContext>()
// Token providers back password-reset tokens (UserManager.GeneratePasswordResetTokenAsync).
.AddDefaultTokenProviders();

// Per-IP fixed-window rate limit on the auth endpoints — a second, transport-level
// layer of brute-force protection in front of the per-account lockout above.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Safety-net global cap per client IP: generous enough that a real user never
    // hits it (a page load fires <10 API calls), but bounds scripted abuse on the
    // otherwise-unthrottled endpoints. Health endpoints opt out via DisableRateLimiting().
    // NOTE: behind the App Service front end, RemoteIpAddress is only the real client
    // once ASPNETCORE_FORWARDEDHEADERS_ENABLED=true is set (deployTODO.md §6).
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 300,
                Window      = TimeSpan.FromMinutes(1),
                QueueLimit  = 0,
            }));

    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window      = TimeSpan.FromMinutes(1),
                QueueLimit  = 0,
            }));
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ClockSkew = TimeSpan.Zero,
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly",     policy => policy.RequireClaim("role", "Admin"));
    options.AddPolicy("AlphaOrAbove",  policy => policy.RequireClaim("role", "Alpha", "Admin"));
    options.AddPolicy("BetaOrAbove",   policy => policy.RequireClaim("role", "Beta", "Alpha", "Admin"));
});

builder.Services.AddScoped<CachedIRacingClient>();
builder.Services.AddScoped<MemberContext>();
builder.Services.AddScoped<MemberStatsService>();
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
builder.Services.AddScoped<AuthService>();

var acsConnectionString = builder.Configuration["ACS_CONNECTION_STRING"];
if (!string.IsNullOrWhiteSpace(acsConnectionString))
{
    builder.Services.AddSingleton(new EmailClient(acsConnectionString));
    builder.Services.AddScoped<IEmailSender, AcsEmailSender>();
}
else
{
    builder.Services.AddScoped<IEmailSender, LoggingEmailSender>();
}

builder.Services.AddScoped<TelemetryUploadService>();
builder.Services.AddScoped<PersonalLapService>();

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
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    // Ensure all roles exist
    foreach (var roleName in new[] { "Standard", "Beta", "Alpha", "Admin" })
    {
        if (!await roleManager.RoleExistsAsync(roleName))
            await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
    }

    // Promote any users listed in ADMIN_SEED_EMAILS (Key Vault: ADMIN-SEED-EMAILS)
    var adminEmails = (app.Configuration["ADMIN_SEED_EMAILS"] ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    foreach (var email in adminEmails)
    {
        var adminUser = await userManager.FindByEmailAsync(email);
        if (adminUser is null) continue;

        var currentRoles = await userManager.GetRolesAsync(adminUser);
        if (currentRoles.Contains("Admin")) continue;

        await userManager.RemoveFromRolesAsync(adminUser, currentRoles);
        await userManager.AddToRoleAsync(adminUser, "Admin");
    }

    // Purge refresh tokens that expired more than 30 days ago so the table does not
    // grow without bound (revoked/expired rows are otherwise never deleted).
    var authService = scope.ServiceProvider.GetRequiredService<AuthService>();
    await authService.PurgeExpiredRefreshTokensAsync(TimeSpan.FromDays(30));
}

// First in the pipeline so it wraps every downstream component and turns any
// unhandled exception into an RFC-7807 problem+json response.
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Before UseStaticFiles so SPA assets get the headers too.
app.UseMiddleware<SecurityHeadersMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "ApexRacers API v1"));
    app.UseCors("ViteDev");
}

// Serve the React SPA from wwwroot (populated by the Docker build).
app.UseStaticFiles();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

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
