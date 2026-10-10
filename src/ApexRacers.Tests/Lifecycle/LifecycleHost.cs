using System.Collections.Concurrent;
using System.Net;
using ApexRacers.Api.Middleware;
using ApexRacers.Api.Services;
using ApexRacers.Core;
using ApexRacers.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ApexRacers.Tests.Lifecycle;

internal static class SyntheticLifecycleActors
{
    public static readonly Guid Owner = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    public static readonly Guid Other = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    public static readonly Guid ProofId = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
    public static readonly DriverScope Scope = new(Owner, 123456, DataProvenance.Demo);
    public static readonly DateTimeOffset ProofAt = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
}

internal sealed class SyntheticLifecycleProof(LifecycleGates gates) : IDriverOwnershipProof
{
    public Task<VerifiedDriverProof?> VerifyAsync(DriverScope scope, CancellationToken ct = default) =>
        Task.FromResult<VerifiedDriverProof?>(scope == SyntheticLifecycleActors.Scope && !gates.IsFaulted("proof-unavailable")
            ? new VerifiedDriverProof(SyntheticLifecycleActors.ProofId,
                gates.IsFaulted("proof-conflict") ? scope with { UserId = SyntheticLifecycleActors.Other } : scope,
                SyntheticLifecycleActors.ProofAt, "controlled-synthetic-proof-v1", "Synthetic Owner")
            : null);
}

internal sealed class HttpLifecycleObserver(LifecycleGates gates) : IDriverLifecycleObserver
{
    public async Task PhaseAsync(string phase, Guid id, CancellationToken ct)
    {
        await gates.ReachAsync(id.ToString(), phase, ct);
        if (gates.IsFaulted(phase))
            throw new InvalidOperationException("Injected synthetic lifecycle interruption.");
    }
}

internal sealed class HttpPublicationObserver(
    LifecycleGates gates, string writer, ConcurrentDictionary<string, Guid> admissions) : IDriverPublicationObserver
{
    public async Task PhaseAsync(string phase, Guid id, CancellationToken ct)
    {
        if (phase == "admitted")
            admissions[writer] = id;
        if (phase == "transport-ended")
            gates.End(writer);
        await gates.ReachAsync(writer, phase, ct);
    }
}

/// <summary>Actual application modules in a loopback-only synthetic host; ordinary product startup is never loaded.</summary>
internal static class LifecycleHost
{
    public static async Task RunAsync()
    {
        var primary = RequireDatabase("DRIVER_LIFECYCLE_DATABASE", "apexracers_lifecycle_primary_");
        var journalConnection = RequireDatabase("DRIVER_LIFECYCLE_JOURNAL", "apexracers_lifecycle_journal_");
        var incarnation = Guid.Parse(Environment.GetEnvironmentVariable("DRIVER_LIFECYCLE_INCARNATION")!);
        var gates = new LifecycleGates();
        var admissions = new ConcurrentDictionary<string, Guid>();
        var backends = new ConcurrentDictionary<string, int>();
        var journal = new PersistedSyntheticJournal(journalConnection, gates);
        var proof = new SyntheticLifecycleProof(gates);
        var observer = new HttpLifecycleObserver(gates);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddSingleton(new IRacingDataScope(DataProvenance.Real));
        builder.Services.AddScoped<LegacyDriverAccessGuard>();
        builder.Services.AddControllers(options => options.Filters.AddService<LegacyDriverAccessGuard>())
            .AddApplicationPart(typeof(LifecycleLegacyProbeController).Assembly)
            .AddApplicationPart(typeof(ApexRacers.Api.Controllers.StandingsController).Assembly);
        builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(primary)
            .AddInterceptors(new LifecycleCheckpointInterceptor(gates, admissions, backends)));
        await using var app = builder.Build();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        // Dedicated synthetic host principal; ordinary product hosts use validated JWTs.
        app.Use((context, next) =>
        {
            var user = context.Request.Path.StartsWithSegments("/sharing") ? SyntheticLifecycleActors.Other
                : context.Request.Query["actor"] == "other" ? SyntheticLifecycleActors.Other : SyntheticLifecycleActors.Owner;
            context.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub, user.ToString())], "controlled-synthetic-host"));
            return next(context);
        });

        DriverAuthorityStore Store(HttpContext context) => new(context.RequestServices.GetRequiredService<AppDbContext>(), TimeProvider.System);
        DriverAuthorization Authority(HttpContext context) => new(Store(context), journal, proof, TimeProvider.System, observer);
        DriverPublication Publication(HttpContext context, string id) =>
            new(Authority(context), Store(context), journal, incarnation, new HttpPublicationObserver(gates, id, admissions), operating: new SyntheticDriverOperatingStore(context.RequestServices.GetRequiredService<AppDbContext>(), TimeProvider.System));

        app.MapPost("/grant", async (HttpContext context, string? consent) =>
        {
            var selected = consent == "both"
                ? new DriverConsent(DriverAuthorizationPolicy.PersonalConsentVersion, DriverAuthorizationPolicy.SharingConsentVersion)
                : new DriverConsent(consent == "none" ? "" : DriverAuthorizationPolicy.PersonalConsentVersion);
            await Authority(context).GrantAsync(SyntheticLifecycleActors.Scope, selected, context.RequestAborted);
            return Results.Ok(new { granted = true });
        });
        app.MapPost("/grant-real", async (HttpContext context) =>
        {
            await Authority(context).GrantAsync(SyntheticLifecycleActors.Scope with { Provenance = DataProvenance.Real },
                new DriverConsent(DriverAuthorizationPolicy.PersonalConsentVersion), context.RequestAborted);
            return Results.Ok();
        });
        AppDbContext UploadDb() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(primary).Options,
            new IRacingDataScope(DataProvenance.Demo));
        app.MapPost("/upload", async (HttpContext context, string? actor) =>
        {
            await using var db = UploadDb();
            var scope = actor == "other" ? SyntheticLifecycleActors.Scope with { UserId = SyntheticLifecycleActors.Other }
                : SyntheticLifecycleActors.Scope;
            var authority = new DriverAuthorization(new(db, TimeProvider.System), journal, proof, TimeProvider.System);
            var upload = new TelemetryUploadService(db, authority, new(db, TimeProvider.System, journal));
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            var file = form.Files.GetFile("file") ?? throw new ArgumentException("A synthetic file is required.");
            var result = await upload.ProcessSyntheticAsync(file.OpenReadStream(), scope, context.RequestAborted);
            context.Response.Headers.CacheControl = "no-store";
            return Results.Json(result);
        });
        app.MapGet("/uploaded-bests/{id}", async (string id, string? actor, HttpContext context) =>
        {
            await using var db = UploadDb();
            var scope = actor == "other" ? SyntheticLifecycleActors.Scope with { UserId = SyntheticLifecycleActors.Other }
                : SyntheticLifecycleActors.Scope;
            var store = new DriverAuthorityStore(db, TimeProvider.System);
            var authority = new DriverAuthorization(store, journal, proof, TimeProvider.System);
            var publication = new DriverPublication(authority, store, journal, incarnation, new HttpPublicationObserver(gates, id, admissions), operating: new SyntheticDriverOperatingStore(db, TimeProvider.System));
            var result = await publication.ReadSyntheticUploadedBestsAsync(scope, new(db, TimeProvider.System, journal), context.RequestAborted);
            await result.ExecuteResultAsync(new ActionContext { HttpContext = context });
        });
        app.MapGet("/private/{id}", async (string id, string? actor, HttpContext context) =>
        {
            var scope = actor == "other" ? SyntheticLifecycleActors.Scope with { UserId = SyntheticLifecycleActors.Other }
                : SyntheticLifecycleActors.Scope;
            var result = await Publication(context, id).ReadSyntheticOwnerAsync(scope, context.RequestAborted);
            await result.ExecuteResultAsync(new ActionContext { HttpContext = context });
        });
        app.MapGet("/sharing/{id}", async (string id, string? actor, HttpContext context) =>
        {
            var recipient = actor == "visitor" ? Guid.Empty : SyntheticLifecycleActors.Other;
            var result = await Publication(context, id).ReadSyntheticSharingAsync(SyntheticLifecycleActors.Scope, recipient, context.RequestAborted);
            await result.ExecuteResultAsync(new ActionContext { HttpContext = context });
        });
        app.MapPost("/transition/{operationId:guid}/{kind}", async (Guid operationId, DriverLifecycleKind kind, HttpContext context) =>
        {
            var result = await Authority(context).TransitionAsync(SyntheticLifecycleActors.Scope, kind, operationId, context.RequestAborted);
            return Results.Json(result, statusCode: result.Completed ? 200 : 202);
        });
        app.MapPost("/copy/{id}", async (string id, HttpContext context) =>
        {
            var authority = Authority(context);
            var access = await authority.ResolveAsync(SyntheticLifecycleActors.Scope, DriverConsentScope.Personal, context.RequestAborted);
            if (access is null) return Results.StatusCode(503);
            await gates.ReachAsync(id, "copy-prepared", context.RequestAborted);
            await new CopyLifecycle(authority, Store(context), journal).CommitAsync(access, "synthetic-private-copy", context.RequestAborted);
            return Results.Ok();
        });
        app.MapPost("/control/hold/{id}/{phase}", (string id, string phase) =>
        {
            gates.Hold(id, phase);
            return Results.Ok();
        });
        app.MapPost("/control/release/{id}/{phase}", (string id, string phase) => gates.Release(id, phase) ? Results.Ok() : Results.BadRequest());
        app.MapGet("/control/wait/{id}/{phase}", async (string id, string phase, HttpContext context) =>
        {
            await gates.WaitAsync(id, phase, context.RequestAborted);
            return Results.Ok();
        });
        app.MapPost("/control/fault/{phase}", (string phase) => { gates.SetFault(phase); return Results.Ok(); });
        app.MapPost("/control/clear/{phase}", (string phase) => { gates.ClearFault(phase); return Results.Ok(); });
        app.MapGet("/control/events", () => gates.Events);
        app.MapGet("/control/backend/{id}", (string id) => backends.TryGetValue(id, out var pid) ? Results.Ok(pid) : Results.NotFound());
        app.MapPost("/control/retry-checkpoint/{id}", async (string id, HttpContext context) =>
        {
            if (!gates.HasEnded(id) || !admissions.TryGetValue(id, out var admission))
                return Results.Conflict();
            await Store(context).CheckpointAsync(admission, incarnation, context.RequestAborted);
            await gates.ReachAsync(id, "checkpoint-recovered", context.RequestAborted);
            return Results.Ok();
        });
        app.MapControllers();
        await app.StartAsync(lifetime.Token);
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        Console.WriteLine($"LIFECYCLE_READY {address}");
        await app.WaitForShutdownAsync(lifetime.Token);
    }

    private static string RequireDatabase(string variable, string prefix)
    {
        var connectionString = Environment.GetEnvironmentVariable(variable)
            ?? throw new InvalidOperationException("Isolated synthetic database is required.");
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (builder.Database is null || !builder.Database.StartsWith(prefix, StringComparison.Ordinal)
            || builder.Host is not ("127.0.0.1" or "localhost"))
            throw new InvalidOperationException("Only a unique loopback synthetic database is permitted.");
        return connectionString;
    }
}

// An actual MVC action that would reveal an identifying/private marker if the legacy resource fence failed.
[ApiController]
[Route("legacy-real")]
public sealed class LifecycleLegacyProbeController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { customerId = SyntheticLifecycleActors.Scope.CustomerId, privateHistory = "forbidden-legacy-marker" });
}
