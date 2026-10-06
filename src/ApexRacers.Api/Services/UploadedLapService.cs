using ApexRacers.Api.Dtos;
using ApexRacers.Core;

namespace ApexRacers.Api.Services;

/// <summary>The legacy User-ID-only interface cannot establish private Driver authority.
/// Controlled owner reads use DriverPublication and PrivateUploadStore with protected dispatch.</summary>
public sealed class UploadedLapService
{
    public Task<List<UploadedBestDto>> GetUploadedBestsAsync(Guid userId, CancellationToken ct = default) =>
        Task.FromException<List<UploadedBestDto>>(new EvidenceCopyUnavailableException());
}
