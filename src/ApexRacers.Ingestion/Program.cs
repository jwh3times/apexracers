using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using ApexRacers.Data;
using ApexRacers.Ingestion;
using Aydsko.iRacingData;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

var keyVaultUrl = builder.Configuration["AZURE_KEY_VAULT_URL"];
if (!string.IsNullOrEmpty(keyVaultUrl))
{
    builder.Configuration.AddAzureKeyVault(
        new Uri(keyVaultUrl),
        new DefaultAzureCredential(),
        new HyphenToUnderscoreSecretManager());
}

// ── Database ────────────────────────────────────────────────────────────────
var connectionString =
    builder.Configuration["DATABASE_CONNECTION_STRING"]
    ?? throw new InvalidOperationException("DATABASE_CONNECTION_STRING is not set.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// ── iRacing Data API — Password Limited OAuth flow ──────────────────────────
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

// Key Vault secret names use hyphens (e.g. IRACING-USERNAME); this maps them
// back to the underscore-style keys the rest of the app expects.
class HyphenToUnderscoreSecretManager : KeyVaultSecretManager
{
    public override string GetKey(KeyVaultSecret secret) =>
        secret.Name.Replace('-', '_');
}
