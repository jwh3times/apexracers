using System.Net;
using System.Net.Http.Json;
using ApexRacers.Api.Dtos;
using ApexRacers.Tests.Helpers;
using Xunit;

namespace ApexRacers.Tests.References;

[Collection(PostgreSqlCollection.Name)]
public sealed class DriverBrowserHttpTests(PostgreSqlFixture fixture)
{
    [Fact]
    public Task Withdrawal_is_bound_to_the_authenticated_owner_and_replays_the_original_durable_operation() =>
        ReferenceTopology.Run(fixture, "browser-withdrawal", async test =>
        {
            var id = Guid.NewGuid();
            async Task<HttpResponseMessage> Withdraw(Guid actor)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/api/drivers/privacy/withdrawal");
                request.Headers.Authorization = new("Bearer", ReferenceActors.Token(actor));
                request.Content = JsonContent.Create(new { operationId = id, scope = "personal" });
                return await test.First.Client.SendAsync(request, test.Token);
            }
            using var recorded = await Withdraw(ReferenceActors.Recipient);
            Assert.Equal(HttpStatusCode.OK, recorded.StatusCode);
            Assert.True(recorded.Headers.CacheControl?.NoStore);
            var body = await recorded.Content.ReadAsStringAsync(test.Token);
            Assert.Contains("\"withdrawalRecorded\":true", body);
            Assert.Contains("\"liveErasureVerified\":false", body);
            using var denied = await test.Request("/api/drivers/scoped/personal", "personal-withdrawn");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, denied.StatusCode);
            using var replay = await Withdraw(ReferenceActors.Recipient);
            Assert.Equal(body, await replay.Content.ReadAsStringAsync(test.Token));
            using var foreign = await Withdraw(ReferenceActors.Target);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, foreign.StatusCode);
        });
    [Fact]
    public Task Personal_evidence_is_authenticated_protected_and_survives_another_Drivers_sharing_withdrawal() =>
        ReferenceTopology.Run(fixture, "browser-personal", async test =>
        {
            using var first = await test.Request("/api/drivers/scoped/personal", "personal-first");
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.True(first.Headers.CacheControl?.NoStore);
            var data = await first.Content.ReadFromJsonAsync<ScopedDriverDetailDto>(test.Token);
            Assert.Equal("Synthetic Reference Owner", data!.DriverName);
            Assert.Equal(89.90, data.OfficialBestLapSeconds);
            await test.Wait("personal-first", "checkpointed");
            await test.Transition(ReferenceActors.Target, Core.DriverLifecycleKind.WithdrawSharing);
            using var next = await test.Request("/api/drivers/scoped/personal", "personal-next");
            Assert.Equal(HttpStatusCode.OK, next.StatusCode);
            Assert.Equal(data, await next.Content.ReadFromJsonAsync<ScopedDriverDetailDto>(test.Token));
            await test.Wait("personal-next", "checkpointed");
        });
}
