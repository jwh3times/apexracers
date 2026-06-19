namespace ApexRacers.Api.Services;

/// <summary>
/// Downloads iRacing standings "chunk" files (the signed S3 documents referenced by a
/// response's <c>chunk_info</c>). Abstracted so services can be unit-tested without HTTP.
/// </summary>
public interface IChunkDownloader
{
    Task<IReadOnlyList<string>> DownloadAsync(
        string baseDownloadUrl, IEnumerable<string> chunkFileNames, CancellationToken ct);
}

/// <summary>
/// <see cref="IChunkDownloader"/> over a typed <see cref="HttpClient"/>. The chunk URLs are
/// pre-signed (no auth header needed); the SDK is only used to obtain the fresh signed names.
/// </summary>
public sealed class HttpChunkDownloader(HttpClient http) : IChunkDownloader
{
    public async Task<IReadOnlyList<string>> DownloadAsync(
        string baseDownloadUrl, IEnumerable<string> chunkFileNames, CancellationToken ct)
    {
        var docs = new List<string>();
        foreach (var name in chunkFileNames)
            docs.Add(await http.GetStringAsync(baseDownloadUrl + name, ct));
        return docs;
    }
}
