using ApexRacers.Api.Services;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Aydsko.iRacingData;
using Aydsko.iRacingData.Common;
using Aydsko.iRacingData.Stats;
using NSubstitute;
using Xunit;

namespace ApexRacers.Tests.Services;

public class WorldRecordServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class StubServiceProvider(object? service) : IServiceProvider
    {
        public object? GetService(Type serviceType) => service;
    }

    private static WorldRecordEntry Entry(
        double? qual = null, double? race = null, double? tt = null, double? practice = null) => new()
    {
        QualifyLapTime = qual is null ? null : TimeSpan.FromSeconds(qual.Value),
        RaceLapTime = race is null ? null : TimeSpan.FromSeconds(race.Value),
        TimeTrialLapTime = tt is null ? null : TimeSpan.FromSeconds(tt.Value),
        PracticeLapTime = practice is null ? null : TimeSpan.FromSeconds(practice.Value),
    };

    // ── FastestLapSeconds (pure) ──────────────────────────────────────────────

    [Fact]
    public void FastestLapSeconds_PicksMinAcrossEntriesAndSessions()
    {
        var best = WorldRecordService.FastestLapSeconds(
        [
            Entry(qual: 67.1, race: 66.8),
            Entry(qual: 66.3, tt: 65.9), // 65.9 is the outright fastest
            Entry(practice: 67.0),
        ]);

        Assert.Equal(65.9, best!.Value, precision: 3);
    }

    [Fact]
    public void FastestLapSeconds_IgnoresNullAndNonPositive()
    {
        var best = WorldRecordService.FastestLapSeconds([Entry(qual: -1, race: 0), Entry(race: 70.0)]);
        Assert.Equal(70.0, best!.Value, precision: 3);
    }

    [Fact]
    public void FastestLapSeconds_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(WorldRecordService.FastestLapSeconds(null));
        Assert.Null(WorldRecordService.FastestLapSeconds([]));
        Assert.Null(WorldRecordService.FastestLapSeconds([Entry(qual: -1)]));
    }

    // ── GetWorldRecordLapSecondsAsync ─────────────────────────────────────────

    [Fact]
    public async Task GetWorldRecordLapSecondsAsync_NotConfigured_ReturnsNullWithoutFetch()
    {
        await using var db = DbContextFactory.Create();
        var service = new WorldRecordService(new CachedIRacingClient(db, new StubServiceProvider(null)));

        Assert.Null(await service.GetWorldRecordLapSecondsAsync(132, 532, Ct));
    }

    [Fact]
    public async Task GetWorldRecordLapSecondsAsync_Configured_ReturnsFastestAndCachesOnce()
    {
        await using var db = DbContextFactory.Create();
        var client = Substitute.For<IDataClient>();
        client.GetWorldRecordsAsync(
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new DataResponse<(WorldRecordsHeader, WorldRecordEntry[])>
            {
                Data = (new WorldRecordsHeader(), [Entry(qual: 66.3, race: 66.8), Entry(tt: 65.95)]),
            });
        var service = new WorldRecordService(new CachedIRacingClient(db, new StubServiceProvider(client)));

        var first = await service.GetWorldRecordLapSecondsAsync(132, 532, Ct);
        var second = await service.GetWorldRecordLapSecondsAsync(132, 532, Ct);

        Assert.Equal(65.95, first!.Value, precision: 3);
        Assert.Equal(first, second);
        await client.Received(1).GetWorldRecordsAsync(
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }
}
