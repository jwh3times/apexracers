using System.Collections.Concurrent;
using System.Net;
using ApexRacers.Api.Middleware;
using ApexRacers.Api.Services;
using ApexRacers.Core;
using ApexRacers.Data;
using ApexRacers.Tests.Lifecycle;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace ApexRacers.Tests.Publication;

internal static class PublicationLedgerHost
{
    public static async Task RunAsync()
    {
        var primary = Environment.GetEnvironmentVariable("DRIVER_LIFECYCLE_DATABASE")!;
        var independent = Environment.GetEnvironmentVariable("DRIVER_LIFECYCLE_JOURNAL")!;
        foreach (var (connection, prefix) in new[] { (primary, "apexracers_ledger_primary_"), (independent, "apexracers_ledger_history_") })
        {
            var target = new NpgsqlConnectionStringBuilder(connection);
            if (target.Host is not ("127.0.0.1" or "localhost") || target.Database?.StartsWith(prefix, StringComparison.Ordinal) != true)
                throw new InvalidOperationException("Unique loopback controlled ledger databases are required.");
        }
        var epoch = Guid.Parse(Environment.GetEnvironmentVariable("PUBLICATION_HISTORY_EPOCH")!);
        var incarnation = Guid.Parse(Environment.GetEnvironmentVariable("DRIVER_LIFECYCLE_INCARNATION")!);
        var gates = new LifecycleGates();
        var admissions = new ConcurrentDictionary<string, Guid>();
        var backends = new ConcurrentDictionary<string, int>();
        var history = new ControlledPublicationHistory(independent, epoch, gates);
        ControlledCohortEvidence genesis;
        await using (var initial = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(primary).Options, new IRacingDataScope(DataProvenance.Demo)))
        {
            var original = await initial.Database.SqlQueryRaw<Guid>("SELECT copy_id AS \"Value\" FROM controlled_fixture_genesis").SingleAsync();
            var marker = await initial.EvidenceCopyMarkers.AsNoTracking().SingleAsync(m => m.Id == original);
            var purpose = await initial.EvidencePurposes.AsNoTracking().SingleAsync(p => p.Id == marker.PurposeId);
            genesis = new(marker.Id, marker.Version, purpose.Id, purpose.Generation, purpose.EvidenceVersion, marker.OriginalAcquiredAt, purpose.CreatedAt);
        }
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0));
        builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(primary)
            .AddInterceptors(new PublicationCheckpointInterceptor(gates, admissions, backends)));
        builder.Services.AddSingleton(new IRacingDataScope(DataProvenance.Demo));
        builder.Services.AddControllers();
        await using var app = builder.Build();
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        PublicationReleaseStore Releases(AppDbContext db, string? writer = null) => new(db, TimeProvider.System, history, new FiniteCompositionReview(gates, writer), epoch);
        app.MapGet("/publish/{id}/{purpose}", async (string id, PublicationPurpose purpose, int? offset, string? actor,
            string? catalog, HttpContext http) =>
        {
            var db = http.RequestServices.GetRequiredService<AppDbContext>();
            Guid? recipient = purpose == PublicationPurpose.Aggregate ? null : actor switch
            {
                "owner" => CohortActors.Owner,
                "owner2" => CohortActors.OtherOwner,
                "missing" => Guid.Parse("dddddddd-3740-4000-8000-000000000099"),
                _ => CohortActors.Recipient
            };
            IDriverEnforcementJournal selectedJournal = gates.IsFaulted("separate-journal")
                ? new PersistedSyntheticJournal(independent, gates) : history;
            var protectedModule = new ControlledCohortPublication(new ControlledCohortSqlSource(db, gates), new FiniteCohortCatalog(gates, genesis),
                Releases(db, id), selectedJournal, incarnation, new HttpPublicationObserver(gates, id, admissions));
            var result = await protectedModule.ReadAsync(new(catalog ?? WholeCohortCandidates.CatalogId, purpose, offset ?? 0), recipient, http.RequestAborted);
            try { await result.ExecuteResultAsync(new ActionContext { HttpContext = http }); }
            finally { await gates.ReachAsync(id, "request-ended", CancellationToken.None); }
        });
        app.MapPost("/reconcile", async (HttpContext http) =>
            await Results.Json(await Releases(http.RequestServices.GetRequiredService<AppDbContext>()).ReconcileHistoryAsync(http.RequestAborted)).ExecuteAsync(http));
        app.MapPost("/control/hold/{id}/{phase}", (string id, string phase) => { gates.Hold(id, phase); return Results.Ok(); });
        app.MapPost("/control/release/{id}/{phase}", (string id, string phase) => gates.Release(id, phase) ? Results.Ok() : Results.BadRequest());
        app.MapGet("/control/wait/{id}/{phase}", async (string id, string phase, HttpContext http) =>
        { await gates.WaitAsync(id, phase, http.RequestAborted); return Results.Ok(); });
        app.MapPost("/control/fault/{phase}", (string phase) => { gates.SetFault(phase); return Results.Ok(); });
        app.MapPost("/control/clear/{phase}", (string phase) => { gates.ClearFault(phase); return Results.Ok(); });
        app.MapGet("/control/backend/{id}", (string id) => backends.TryGetValue(id, out var backend) ? Results.Ok(backend) : Results.NotFound());
        app.MapPost("/control/retry-checkpoint/{id}", async (string id, HttpContext http) =>
        {
            if (!gates.HasEnded(id) || !admissions.TryGetValue(id, out var admission)) return Results.Conflict();
            var entry = (await history.ReadAsync(http.RequestAborted)).Releases.Single(r => r.Proposal.Id == admission);
            if (entry.Proposal.Incarnation != incarnation) return Results.Conflict();
            await Releases(http.RequestServices.GetRequiredService<AppDbContext>()).CheckpointAsync(entry, false, http.RequestAborted);
            return Results.Ok();
        });
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        Console.WriteLine("LIFECYCLE_READY " + address);
        await app.WaitForShutdownAsync();
    }
}
