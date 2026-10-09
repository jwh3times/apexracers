using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ApexRacers.Api.Dtos;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using ApexRacers.Tests.Lifecycle;
using ApexRacers.Tests.Publication;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace ApexRacers.Tests.References;

internal sealed class ReferenceTopology(string primary, string independent, Guid epoch, LifecycleProcess first,
    LifecycleProcess second, CancellationToken ct) : IAsyncDisposable
{
    public LifecycleProcess First { get; } = first;
    public LifecycleProcess Second { get; } = second;
    public CancellationToken Token => ct;
    public Guid HistoryEpoch => epoch;
    public ControlledPublicationHistory History { get; } = new(independent, epoch, new LifecycleGates());
    public AppDbContext Db(DataProvenance provenance = DataProvenance.Demo) => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(primary).Options, new IRacingDataScope(provenance));
    public async Task<HttpResponseMessage> Request(string path, string id, Guid? actor = null, string? reference = null, bool secondHost = false, bool anonymous = false)
    {
        using var request = new HttpRequestMessage(reference is null ? HttpMethod.Get : HttpMethod.Post, path);
        if (!anonymous) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ReferenceActors.Token(actor ?? ReferenceActors.Recipient));
        request.Headers.Add("X-Rehearsal-Writer", id);
        if (reference is not null) request.Content = JsonContent.Create(new ScopedDriverReferenceRequest(reference));
        return await (secondHost ? Second : First).Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }
    public async Task<ScopedDriverDiscoveryDto> Discover(string id = "discovery", bool secondHost = false, bool follows = false)
    {
        using var response = await Request("/api/drivers/scoped/" + (follows ? "follows" : "discovery"), id, secondHost: secondHost);
        response.EnsureSuccessStatusCode();
        var rows = await response.Content.ReadFromJsonAsync<ScopedDriverDiscoveryDto[]>(ct);
        await Wait(id, "checkpointed", secondHost);
        return Assert.Single(rows!);
    }
    public async Task Control(string path, bool secondHost = false)
    {
        using var response = await (secondHost ? Second : First).ControlClient.PostAsync("/control/" + path, null, ct);
        response.EnsureSuccessStatusCode();
    }
    public async Task Wait(string id, string phase, bool secondHost = false)
    {
        using var response = await (secondHost ? Second : First).ControlClient.GetAsync($"/control/wait/{id}/{phase}", ct);
        response.EnsureSuccessStatusCode();
    }
    public async Task Time(DateTimeOffset at, bool both = true)
    {
        var path = "time?at=" + Uri.EscapeDataString(at.ToString("O"));
        await Control(path); if (both) await Control(path, true);
    }
    public async Task<DriverLifecycleOutcome> Transition(Guid user, DriverLifecycleKind kind, Guid? operation = null)
    {
        using var response = await Second.ControlClient.PostAsync($"/control/transition/{user}/{kind}/{operation ?? Guid.NewGuid()}", null, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DriverLifecycleOutcome>(ct))!;
    }
    public async Task Grant(Guid user, ReferenceClock clock, bool sharing = true, bool fresh = false)
    {
        await using var db = Db();
        var store = new DriverAuthorityStore(db, clock);
        var proof = new VerifiedDriverProof(Guid.NewGuid(), ReferenceActors.Scope(user), clock.GetUtcNow().AddSeconds(-1),
            "controlled375-synthetic-proof", user == ReferenceActors.Target ? "Synthetic Reference Driver" : "Synthetic Reference Owner");
        var consent = new DriverConsent(DriverAuthorizationPolicy.PersonalConsentVersion, sharing ? DriverAuthorizationPolicy.SharingConsentVersion : null);
        if (fresh) await store.GrantFreshCollectionAsync(proof, consent, History, ct);
        else await store.GrantAsync(proof, consent, History, ct);
    }
    public static async Task Run(PostgreSqlFixture fixture, string name, Func<ReferenceTopology, Task> exercise)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var ct = timeout.Token;
        await using var test = await CreateAsync(fixture.Container.GetConnectionString(), ct, browser: name.StartsWith("browser-", StringComparison.Ordinal));
        var epoch = test.HistoryEpoch;
        var first = test.First; var second = test.Second;
        var passed = false;
        try { await exercise(test); passed = true; }
        finally
        {
            var output = Path.Combine("TestResults", "driver-references"); Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(Path.Combine(output, name + ".json"), JsonSerializer.Serialize(new
            {
                Scenario = name,
                Passed = passed,
                Catalog = DriverReferences.CatalogId,
                Fixture = name.StartsWith("browser-", StringComparison.Ordinal) ? "controlled-browser-template-v1" : "controlled-reference-template-v1",
                Epoch = epoch,
                Hosts = new[] { first, second }.Select(h => new { h.ProcessId, h.Incarnation }),
                Boundary = "Fabricated reference fixtures, actual JWT/production controller/owned response executor, independent PostgreSQL. No live/catalog admission."
            }), CancellationToken.None);
        }
    }
    public static async Task<ReferenceTopology> CreateAsync(string connection, CancellationToken ct, bool browser = false, int port = 0)
    {
        async Task<string> Create(string prefix)
        {
            var name = prefix + Guid.NewGuid().ToString("N");
            await using var c = new NpgsqlConnection(connection); await c.OpenAsync(ct);
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", c); await command.ExecuteNonQueryAsync(ct);
            return new NpgsqlConnectionStringBuilder(connection) { Database = name, Host = "127.0.0.1", Pooling = false }.ConnectionString;
        }
        var primary = await Create("apexracers_reference_primary_"); var independent = await Create("apexracers_reference_history_");
        var epoch = Guid.NewGuid();
        var history = new ControlledPublicationHistory(independent, epoch, new LifecycleGates()); await history.InitializeGenesisAsync(ct);
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(primary).Options, new IRacingDataScope(DataProvenance.Demo)))
        {
            await db.Database.MigrateAsync(ct);
            db.Users.AddRange(new[] { ReferenceActors.Recipient, ReferenceActors.Target, ReferenceActors.Other }.Select(id => new ApplicationUser
            { Id = id, DisplayName = "Controlled synthetic User", EmailConfirmed = true }));
            await db.SaveChangesAsync(ct);
            var grants = new DriverAuthorityStore(db, TimeProvider.System);
            foreach (var user in new[] { ReferenceActors.Recipient, ReferenceActors.Target })
                await grants.GrantAsync(new(Guid.NewGuid(), ReferenceActors.Scope(user), DateTimeOffset.UtcNow.AddMinutes(-1), "controlled375-synthetic-proof",
                    user == ReferenceActors.Target ? "Synthetic Reference Driver" : "Synthetic Reference Owner"),
                    new(DriverAuthorizationPolicy.PersonalConsentVersion, user == ReferenceActors.Target ? DriverAuthorizationPolicy.SharingConsentVersion : null), history, ct);
            await ReferenceActors.SeedAsync(db, ct);
            if (browser)
            {
                var flag = await db.FeatureFlags.SingleAsync(f => f.Key == "iracing-demo", ct);
                flag.IsEnabled = true; flag.MinimumRole = "Standard";
                await db.SaveChangesAsync(ct);
            }
        }
        var first = await LifecycleProcess.StartReferenceAsync(primary, independent, epoch, ct, browser, port);
        LifecycleProcess second;
        try { second = await LifecycleProcess.StartReferenceAsync(primary, independent, epoch, ct, browser); } catch { await first.DisposeAsync(); throw; }
        return new ReferenceTopology(primary, independent, epoch, first, second, ct);
    }
    public async ValueTask DisposeAsync() { await First.DisposeAsync(); await Second.DisposeAsync(); }
}
