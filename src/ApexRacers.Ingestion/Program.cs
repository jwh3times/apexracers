using ApexRacers.Data;
using ApexRacers.Ingestion;
using Aydsko.iRacingData;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

// ── Database ────────────────────────────────────────────────────────────────
var connectionString =
    builder.Configuration["DATABASE_CONNECTION_STRING"]
    ?? throw new InvalidOperationException("DATABASE_CONNECTION_STRING is not set.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// ── iRacing Data API — Password Limited OAuth flow ──────────────────────────
// Credentials are issued directly by iRacing. Contact iRacing support to
// request OAuth client credentials before deploying this worker.
var irUsername = builder.Configuration["IRACING_USERNAME"]
    ?? throw new InvalidOperationException("IRACING_USERNAME is not set.");
var irPassword = builder.Configuration["IRACING_PASSWORD"]
    ?? throw new InvalidOperationException("IRACING_PASSWORD is not set.");
var irClientId = builder.Configuration["IRACING_CLIENT_ID"]
    ?? throw new InvalidOperationException("IRACING_CLIENT_ID is not set.");
var irClientSecret = builder.Configuration["IRACING_CLIENT_SECRET"]
    ?? throw new InvalidOperationException("IRACING_CLIENT_SECRET is not set.");

builder.Services.AddIRacingDataApi(options =>
    options.UsePasswordLimitedOAuth(
        userName: irUsername,
        password: irPassword,
        clientId: irClientId,
        clientSecret: irClientSecret,
        passwordIsEncoded: false,
        clientSecretIsEncoded: false));

// ── Hosted services ─────────────────────────────────────────────────────────
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
