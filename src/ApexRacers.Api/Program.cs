using System.Text;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using ApexRacers.Api.Services;
using ApexRacers.Data;
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

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 8;
    options.Password.RequireNonAlphanumeric = false;
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<AppDbContext>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ClockSkew = TimeSpan.Zero,
        };
    });

builder.Services.AddScoped<SeriesService>();
builder.Services.AddScoped<WeekCarStatsService>();
builder.Services.AddScoped<PercentileCalculationService>();
builder.Services.AddScoped<CarRecommendationService>();
builder.Services.AddScoped<UserAnalyticsService>();
builder.Services.AddScoped<AuthService>();
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
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "ApexRacers API v1"));
    app.UseCors("ViteDev");
}

// Serve the React SPA from wwwroot (populated by the Docker build).
app.UseStaticFiles();

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
