using ApexRacers.Core;
using Xunit;

namespace ApexRacers.Tests.Models;

/// <summary>
/// Locks the relationships between the Telemetry Upload bounds. The values themselves are policy
/// and may change; what must not change is that the transport bound stays strictly above the file
/// bound. If the two ever met, a file at exactly the file bound would be cut off by the framework's
/// form reader and the controller's 413 would be unreachable — which is the whole point of the gap.
/// </summary>
public class TelemetryUploadTests
{
    [Fact]
    public void MaxRequestBytes_LeavesHeadroomAboveTheFileBound()
    {
        Assert.True(
            TelemetryUpload.MaxRequestBytes > TelemetryUpload.MaxFileSizeBytes,
            "The request bound must exceed the file bound, or multipart framing pushes a "
                + "maximum-size file past it.");
    }

    [Fact]
    public void MaxRequestBytes_HeadroomExceedsPlausibleMultipartFramingOverhead()
    {
        // Framing is a boundary marker plus part headers — hundreds of bytes, not kilobytes.
        // 64 KB is a generous floor that still catches the headroom being whittled to nothing.
        var headroom = TelemetryUpload.MaxRequestBytes - TelemetryUpload.MaxFileSizeBytes;
        Assert.True(headroom >= 64 * 1024, $"Headroom of {headroom} bytes is too small to be safe.");
    }

    [Fact]
    public void MaxFileSizeBytes_DerivesFromTheMegabyteBound()
    {
        Assert.Equal(
            TelemetryUpload.MaxFileSizeMegabytes * 1024L * 1024L,
            TelemetryUpload.MaxFileSizeBytes);
    }

    [Fact]
    public void TooLargeMessage_QuotesTheEnforcedLimit()
    {
        // The message is what the caller is told; naming a different number than the one enforced
        // would be worse than saying nothing.
        Assert.Contains(
            $"{TelemetryUpload.MaxFileSizeMegabytes} MB",
            TelemetryUpload.TooLargeMessage);
    }
}
