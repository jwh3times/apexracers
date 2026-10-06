using System.Text.Json;
using ApexRacers.Api.Services;
using ApexRacers.Core;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ApexRacers.Tests.Services;

public sealed class TelemetryUploadServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(DataProvenance.Real, 12345)]
    [InlineData(DataProvenance.Real, 999999)]
    [InlineData(DataProvenance.Demo, 12345)]
    [InlineData(DataProvenance.Unknown, 0)]
    public async Task RecorderAndClaimCannotAuthorizePersistenceOrIdentifyPreview(DataProvenance provenance, int customerId)
    {
        await using var db = DbContextFactory.Create(provenance);
        var userId = Guid.NewGuid();
        db.Users.Add(new() { Id = userId, DisplayName = "Synthetic account", IRacingCustomerId = customerId });
        await db.SaveChangesAsync(Ct);
        var stream = FakeIbtBuilder.Build(laps: 2, lapTime: 90.5f, customerId: customerId);
        var result = await new TelemetryUploadService(db).ProcessAsync(stream, userId, Ct);
        Assert.False(result.Persisted);
        Assert.Equal(2, result.ValidLaps);
        Assert.Equal(90.5, result.BestLapSeconds);
        Assert.Equal(["Persisted", "TotalLaps", "ValidLaps", "BestLapSeconds"],
            JsonSerializer.SerializeToElement(result).EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Empty(await db.UploadedLaps.IgnoreQueryFilters().ToListAsync(Ct));
        Assert.Empty(await db.PrivateUploadSessions.ToListAsync(Ct));
        Assert.Empty(await db.EvidenceCopyMarkers.ToListAsync(Ct));
        Assert.Empty(await db.Cars.ToListAsync(Ct));
        Assert.False(stream.CanRead);
    }

    [Fact]
    public async Task InvalidLapsProduceTransientZeroValidPreview()
    {
        await using var db = DbContextFactory.Create();
        var stream = FakeIbtBuilder.Build(laps: 2, validLaps: false);
        var preview = await new TelemetryUploadService(db).ProcessAsync(stream, Guid.NewGuid(), Ct);
        Assert.Equal(2, preview.TotalLaps);
        Assert.Equal(0, preview.ValidLaps);
        Assert.Null(preview.BestLapSeconds);
        Assert.False(stream.CanRead);
    }

    [Fact]
    public async Task CancellationAndMalformedInputDisposeOwnedRawMaterial()
    {
        await using var db = DbContextFactory.Create();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var canceled = FakeIbtBuilder.Build(laps: 1);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new TelemetryUploadService(db).ProcessAsync(canceled, Guid.NewGuid(), cancellation.Token));
        Assert.False(canceled.CanRead);
        var malformed = new MemoryStream([1, 2, 3]);
        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            new TelemetryUploadService(db).ProcessAsync(malformed, Guid.NewGuid(), Ct));
        Assert.Equal("The telemetry file could not be processed.", error.Message);
        Assert.False(malformed.CanRead);
    }
}
