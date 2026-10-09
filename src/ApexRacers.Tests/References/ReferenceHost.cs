using System.Collections.Concurrent;
using System.Net;
using ApexRacers.Api.Controllers;
using ApexRacers.Api.Middleware;
using ApexRacers.Api.Services;
using ApexRacers.Core;
using ApexRacers.Data;
using ApexRacers.Tests.Lifecycle;
using ApexRacers.Tests.Publication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ApexRacers.Tests.References;

/// <summary>Dedicated test executable host: real JWT authentication and real product controllers.
/// No product startup/configuration can instantiate this synthetic proof/catalog/history fixture.</summary>
internal static class ReferenceHost
{
    public static async Task RunAsync(bool browser = false)
    {
        var primary = Environment.GetEnvironmentVariable("DRIVER_LIFECYCLE_DATABASE")!;
        var independent = Environment.GetEnvironmentVariable("DRIVER_LIFECYCLE_JOURNAL")!;
        foreach (var (connection, prefix) in new[] { (primary, "apexracers_reference_primary_"), (independent, "apexracers_reference_history_") })
        {
            var target = new NpgsqlConnectionStringBuilder(connection);
            if (target.Host is not ("127.0.0.1" or "localhost") || target.Database?.StartsWith(prefix, StringComparison.Ordinal) != true)
                throw new InvalidOperationException("Unique loopback reference fixture databases are required.");
        }
        var epoch = Guid.Parse(Environment.GetEnvironmentVariable("PUBLICATION_HISTORY_EPOCH")!);
        var incarnation = Guid.Parse(Environment.GetEnvironmentVariable("DRIVER_LIFECYCLE_INCARNATION")!);
        var gates = new LifecycleGates();
        var clock = new ReferenceClock();
        var admissions = new ConcurrentDictionary<string, Guid>();
        var history = new ControlledPublicationHistory(independent, epoch, gates);
        ControlledCohortEvidence genesis;
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(primary).Options))
        {
            var copyId = await db.Database.SqlQueryRaw<Guid>("SELECT copy_id AS \"Value\" FROM controlled_reference_genesis").SingleAsync();
            var m = await db.EvidenceCopyMarkers.SingleAsync(m => m.Id == copyId);
            var p = await db.EvidencePurposes.SingleAsync(p => p.Id == m.PurposeId);
            genesis = new(m.Id, m.Version, p.Id, p.Generation, p.EvidenceVersion, m.OriginalAcquiredAt, p.CreatedAt);
        }
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], WebRootPath = browser ? Environment.GetEnvironmentVariable("DRIVER_BROWSER_WEBROOT") : null });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, browser ? int.Parse(Environment.GetEnvironmentVariable("DRIVER_BROWSER_PORT") ?? "0") : 0));
        builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(primary));
        builder.Services.AddScoped(sp => new AppDbContext(sp.GetRequiredService<DbContextOptions<AppDbContext>>(), new IRacingDataScope(DataProvenance.Demo)));
        builder.Services.AddSingleton(new IRacingDataScope(DataProvenance.Real));
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddSingleton<IDriverEnforcementJournal>(history);
        builder.Services.AddSingleton<IDriverOwnershipProof, UnavailableDriverOwnershipProof>();
        builder.Services.AddScoped<DriverAuthorityStore>();
        builder.Services.AddScoped<DriverAuthorization>();
        builder.Services.AddScoped<DriverPrivacy>();
        builder.Services.AddSingleton<IDriverLifecycleObserver>(new HttpLifecycleObserver(gates));
        if (browser)
        {
            builder.Services.AddIdentityCore<ApplicationUser>().AddRoles<Microsoft.AspNetCore.Identity.IdentityRole<Guid>>().AddEntityFrameworkStores<AppDbContext>();
            builder.Services.AddScoped<FeatureFlagEligibility>();
            builder.Services.AddScoped<AdminService>();
        }
        builder.Services.AddScoped<DriverReferenceStore>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<LegacyDriverAccessGuard>();
        builder.Services.AddScoped(sp => new ScopedDriverPublication(sp.GetRequiredService<DriverReferenceStore>(), history, incarnation,
            new ReferenceCatalog(sp.GetRequiredService<AppDbContext>(), genesis, gates, browser),
            new PublicationReleaseStore(sp.GetRequiredService<AppDbContext>(), clock, history, browser ? new BrowserCompositionReview() : new ReferenceCompositionReview(), epoch),
            new HttpPublicationObserver(gates, sp.GetRequiredService<IHttpContextAccessor>().HttpContext!.Request.Headers["X-Rehearsal-Writer"].ToString(), admissions)));
        builder.Services.AddControllers(o => o.Filters.AddService<LegacyDriverAccessGuard>()).AddApplicationPart(typeof(ScopedDriversController).Assembly);
        builder.Services.AddAuthentication("Bearer").AddJwtBearer(o => { o.MapInboundClaims = false; o.TokenValidationParameters = JwtSettings.FromConfiguration(ReferenceActors.Configuration).ValidationParameters(); });
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(o => o.AddPolicy("iracing-search", _ => System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("controlled")));
        await using var app = builder.Build();
        app.Use(async (http, next) =>
        {
            try { await next(http); }
            finally
            {
                var writer = http.Request.Headers["X-Rehearsal-Writer"].ToString();
                if (writer.Length != 0) await gates.ReachAsync(writer, "request-ended", CancellationToken.None);
            }
        });
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter();
        app.MapControllers();
        if (browser)
        {
            app.UseStaticFiles();
            app.MapFallbackToFile("index.html");
            app.MapGet("/control/session/{user}", (Guid user) => user == ReferenceActors.Recipient || user == ReferenceActors.Target
                ? Results.Json(new { token = ReferenceActors.Token(user) }) : Results.BadRequest());
            app.MapPost("/control/browser/renew", async (HttpContext http) =>
            {
                var store = http.RequestServices.GetRequiredService<DriverAuthorityStore>();
                var authorization = http.RequestServices.GetRequiredService<DriverAuthorization>();
                foreach (var user in new[] { ReferenceActors.Recipient, ReferenceActors.Target })
                {
                    var current = await history.ReadCurrentAsync(ReferenceActors.Scope(user), http.RequestAborted);
                    foreach (var intent in current.PendingIntents)
                        if (!(await authorization.RecoverAsync(intent, http.RequestAborted)).Completed) return Results.Conflict();
                }
                clock.Set(clock.GetUtcNow().AddSeconds(10));
                foreach (var user in new[] { ReferenceActors.Recipient, ReferenceActors.Target })
                    await store.GrantFreshCollectionAsync(new(Guid.NewGuid(), ReferenceActors.Scope(user), clock.GetUtcNow().AddSeconds(-1),
                        "controlled376-synthetic-proof", user == ReferenceActors.Target ? "Synthetic Reference Driver" : "Synthetic Reference Owner"),
                        new(DriverAuthorizationPolicy.PersonalConsentVersion, user == ReferenceActors.Target ? DriverAuthorizationPolicy.SharingConsentVersion : null), history, http.RequestAborted);
                return Results.Ok();
            });
        }
        app.MapPost("/control/time", (DateTimeOffset at) => { clock.Set(at); return Results.Ok(); });
        app.MapPost("/control/hold/{id}/{phase}", (string id, string phase) => { gates.Hold(id, phase); return Results.Ok(); });
        app.MapPost("/control/release/{id}/{phase}", (string id, string phase) => gates.Release(id, phase) ? Results.Ok() : Results.BadRequest());
        app.MapGet("/control/wait/{id}/{phase}", async (string id, string phase, HttpContext http) => { await gates.WaitAsync(id, phase, http.RequestAborted); return Results.Ok(); });
        app.MapPost("/control/fault/{phase}", (string phase) => { gates.SetFault(phase); return Results.Ok(); });
        app.MapPost("/control/clear/{phase}", (string phase) => { gates.ClearFault(phase); return Results.Ok(); });
        app.MapPost("/control/cleanup", async (HttpContext http) => Results.Ok(await http.RequestServices.GetRequiredService<DriverReferenceStore>().ReconcileAsync(http.RequestAborted)));
        app.MapPost("/control/transition/{user}/{kind}/{id}", async (Guid user, DriverLifecycleKind kind, Guid id, HttpContext http) =>
        {
            var db = http.RequestServices.GetRequiredService<AppDbContext>();
            var authority = new DriverAuthorization(new(db, clock), history, new UnavailableDriverOwnershipProof(), clock, new HttpLifecycleObserver(gates));
            return Results.Json(await authority.TransitionAsync(ReferenceActors.Scope(user), kind, id, http.RequestAborted));
        });
        await app.StartAsync();
        Console.WriteLine("LIFECYCLE_READY " + app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
        await app.WaitForShutdownAsync();
    }
}
