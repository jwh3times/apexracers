using System.Net;
using System.Net.Http.Json;
using ApexRacers.Core;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ApexRacers.Tests.Lifecycle;

[Collection(PostgreSqlCollection.Name)]
public sealed class PrivateUploadHttpTests(PostgreSqlFixture postgres)
{
    [Fact]
    public Task MultipartSyntheticUploadAndProtectedAllTimeBestRemainOwnedAndUnavailableAfterRestartedWithdrawal() =>
        LifecycleTopology.RunAsync(postgres, "private-upload-positive-withdrawal", ["UPLOAD-01", "AUTH-01", "COPY-03"], async test =>
        {
            await CatalogAsync(test);
            await test.GrantAsync();
            using var content = Multipart();
            using var upload = await test.Publisher.Client.PostAsync("/upload", content, test.CancellationToken);
            upload.EnsureSuccessStatusCode();
            Assert.True((await upload.Content.ReadFromJsonAsync<PrivateUploadOutcome>(test.CancellationToken))!.Persisted);
            using var bests = await test.Publisher.Client.GetAsync("/uploaded-bests/positive", test.CancellationToken);
            bests.EnsureSuccessStatusCode();
            Assert.Equal("no-store", bests.Headers.CacheControl?.ToString());
            Assert.Equal(90, Assert.Single((await bests.Content.ReadFromJsonAsync<PrivateUploadedBest[]>(test.CancellationToken))!).BestLapSeconds);
            await test.WaitAsync("positive", "checkpointed");
            using var other = await test.Publisher.Client.GetAsync("/uploaded-bests/other?actor=other", test.CancellationToken);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, other.StatusCode);
            await test.TransitionAsync(Guid.NewGuid());
            await test.RestartBothAsync();
            using var withdrawn = await test.Publisher.Client.GetAsync("/uploaded-bests/withdrawn", test.CancellationToken);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, withdrawn.StatusCode);
            await using var primary = test.OpenPrimary();
            Assert.Single(await primary.PrivateUploadSessions.ToListAsync(test.CancellationToken));
            Assert.Equal(2, await primary.PrivateUploadedLaps.CountAsync(test.CancellationToken));
        });

    [Fact]
    public Task AdmissionDrainIncludesActualPrivateUploadedBestResponseWriter() =>
        LifecycleTopology.RunAsync(postgres, "private-upload-transport-drain", ["UPLOAD-01", "AUTH-03", "HTTP-01"], async test =>
        {
            await CatalogAsync(test);
            await test.GrantAsync();
            using var content = Multipart();
            using var upload = await test.Publisher.Client.PostAsync("/upload", content, test.CancellationToken);
            upload.EnsureSuccessStatusCode();
            await test.HoldAsync("private-writer", "first-written");
            var read = test.Publisher.Client.GetAsync("/uploaded-bests/private-writer", HttpCompletionOption.ResponseHeadersRead, test.CancellationToken);
            await test.WaitAsync("private-writer", "first-written");
            var id = Guid.NewGuid();
            var pending = await test.TransitionAsync(id);
            Assert.False(pending.Completed);
            Assert.Equal(1, pending.PendingWriters);
            await test.ExpireLeasesAsync();
            Assert.False((await test.TransitionAsync(id)).Completed);
            await test.ReleaseAsync("private-writer", "first-written");
            using var response = await read;
            response.EnsureSuccessStatusCode();
            await response.Content.ReadAsByteArrayAsync(test.CancellationToken);
            await test.WaitAsync("private-writer", "checkpointed");
            Assert.True((await test.TransitionAsync(id)).Completed);
        });

    [Fact]
    public Task WithdrawalBeforeFirstPrivateResponseByteSuppressesPreparedBestsAndCheckpointsWriter() =>
        LifecycleTopology.RunAsync(postgres, "private-upload-unsent-withdrawal", ["UPLOAD-01", "AUTH-03", "HTTP-01"], async test =>
        {
            await CatalogAsync(test);
            await test.GrantAsync();
            using var content = Multipart();
            using var upload = await test.Publisher.Client.PostAsync("/upload", content, test.CancellationToken);
            upload.EnsureSuccessStatusCode();
            await test.HoldAsync("unsent-private", "admitted");
            var read = test.Publisher.Client.GetAsync("/uploaded-bests/unsent-private", test.CancellationToken);
            await test.WaitAsync("unsent-private", "admitted");
            var id = Guid.NewGuid();
            Assert.False((await test.TransitionAsync(id)).Completed);
            await test.ReleaseAsync("unsent-private", "admitted");
            using var response = await read;
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync(test.CancellationToken));
            await test.WaitAsync("unsent-private", "checkpointed");
            Assert.True((await test.TransitionAsync(id)).Completed);
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task NewUploadBeforeFirstPrivateResponseByteSuppressesPreparedEmptyOrOldBest(bool hadUpload) =>
        LifecycleTopology.RunAsync(postgres, $"private-upload-unsent-new-source-{hadUpload}", ["UPLOAD-01", "COPY-03", "HTTP-01"], async test =>
        {
            await CatalogAsync(test);
            await test.GrantAsync();
            if (hadUpload)
            {
                using var original = Multipart();
                using var uploaded = await test.Publisher.Client.PostAsync("/upload", original, test.CancellationToken);
                uploaded.EnsureSuccessStatusCode();
            }
            await test.HoldAsync("unsent-old-inputs", "admitted");
            var read = test.Publisher.Client.GetAsync("/uploaded-bests/unsent-old-inputs", test.CancellationToken);
            await test.WaitAsync("unsent-old-inputs", "admitted");
            using var faster = Multipart(85, 1_000_000);
            using var upload = await test.Publisher.Client.PostAsync("/upload", faster, test.CancellationToken);
            upload.EnsureSuccessStatusCode();
            await test.ReleaseAsync("unsent-old-inputs", "admitted");
            using var response = await read;
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync(test.CancellationToken));
            await test.WaitAsync("unsent-old-inputs", "checkpointed");
            using var current = await test.Publisher.Client.GetAsync("/uploaded-bests/current-inputs", test.CancellationToken);
            current.EnsureSuccessStatusCode();
            Assert.Equal(85, Assert.Single((await current.Content.ReadFromJsonAsync<PrivateUploadedBest[]>(test.CancellationToken))!).BestLapSeconds);
            await test.WaitAsync("current-inputs", "checkpointed");
        });

    private static MultipartFormDataContent Multipart(float seconds = 90, long recordedAt = 0)
    {
        var content = new MultipartFormDataContent();
        content.Add(new StreamContent(FakeIbtBuilder.Build(laps: 2, lapTime: seconds, customerId: SyntheticLifecycleActors.Scope.CustomerId,
            sessionDate: recordedAt)),
            "file", "synthetic-only.ibt");
        return content;
    }
    private static async Task CatalogAsync(LifecycleTopology test)
    {
        await using var db = test.OpenPrimary();
        db.Cars.Add(new() { Id = 99, Name = "Synthetic Catalog Car", NameAbbreviated = "SC" });
        db.Tracks.Add(new() { Id = 42, Name = "Synthetic Catalog Track" });
        await db.SaveChangesAsync(test.CancellationToken);
    }
}
