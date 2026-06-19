using System.Net;
using System.Net.Http;
using ApexRacers.Api.Services;
using Xunit;

namespace ApexRacers.Tests.Services;

public class HttpChunkDownloaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<string> RequestedUrls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            RequestedUrls.Add(url);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                // Echo the chunk file name so we can assert order + URL building.
                Content = new StringContent($"[\"{url}\"]"),
            });
        }
    }

    [Fact]
    public async Task DownloadAsync_BuildsUrls_AndReturnsBodiesInOrder()
    {
        var handler = new CapturingHandler();
        var http = new HttpClient(handler);
        var downloader = new HttpChunkDownloader(http);

        var docs = await downloader.DownloadAsync(
            "https://s3.example/base/", ["one.json?sig=a", "two.json?sig=b"], Ct);

        Assert.Equal(
            new[] { "https://s3.example/base/one.json?sig=a", "https://s3.example/base/two.json?sig=b" },
            handler.RequestedUrls);
        Assert.Equal(2, docs.Count);
        Assert.Contains("one.json", docs[0]);
        Assert.Contains("two.json", docs[1]);
    }

    [Fact]
    public async Task DownloadAsync_NoChunks_ReturnsEmpty()
    {
        var downloader = new HttpChunkDownloader(new HttpClient(new CapturingHandler()));

        var docs = await downloader.DownloadAsync("https://s3.example/base/", [], Ct);

        Assert.Empty(docs);
    }
}
