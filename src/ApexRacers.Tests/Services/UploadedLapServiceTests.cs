using ApexRacers.Api.Services;
using ApexRacers.Core;
using Xunit;

namespace ApexRacers.Tests.Services;

public sealed class UploadedLapServiceTests
{
    [Fact]
    public async Task BareUserIdentifierCannotPublishPrivateUploads()
    {
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() =>
            new UploadedLapService().GetUploadedBestsAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));
    }
}
