using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Xunit;

namespace ApexRacers.Tests.Services;

public class TelemetryUploadServiceTests
{
    // ── Driver attribution ────────────────────────────────────────────────────
    //
    // An accepted upload's laps become the uploader's own pace and are ranked against a field of
    // real race laps, so a file recorded by someone else must not reach the database.

    private static Guid SeedUser(AppDbContext db, long? claimedCustId)
    {
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser
        {
            Id = userId,
            DisplayName = "Jerry",
            IRacingCustomerId = claimedCustId,
        });
        db.SaveChanges();
        return userId;
    }

    [Fact]
    public async Task ProcessAsync_FileRecordedByAnotherDriver_IsRejected()
    {
        await using var db = DbContextFactory.Create();
        var userId = SeedUser(db, claimedCustId: 12345);
        var svc = new TelemetryUploadService(db);

        using var stream = FakeIbtBuilder.Build(laps: 2, customerId: 999999);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ProcessAsync(stream, userId, TestContext.Current.CancellationToken));

        // InvalidOperationException maps to 400 and the middleware surfaces the message, so it
        // names both drivers and what to do about it.
        Assert.Contains("999999", ex.Message);
        Assert.Contains("12345", ex.Message);
    }

    [Fact]
    public async Task ProcessAsync_FileRecordedByAnotherDriver_WritesNothingAtAll()
    {
        await using var db = DbContextFactory.Create();
        var userId = SeedUser(db, claimedCustId: 12345);
        var svc = new TelemetryUploadService(db);

        using var stream = FakeIbtBuilder.Build(laps: 2, customerId: 999999);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ProcessAsync(stream, userId, TestContext.Current.CancellationToken));

        // The rejection happens before the car/track upsert, so a refused upload leaves no trace —
        // not even the catalog rows the happy path creates.
        Assert.Empty(db.UploadedLaps);
        Assert.Empty(db.Cars);
        Assert.Empty(db.Tracks);
    }

    [Fact]
    public async Task ProcessAsync_FileMatchesClaimedIdentity_StoresTheRecordingDriver()
    {
        await using var db = DbContextFactory.Create();
        var userId = SeedUser(db, claimedCustId: 12345);
        var svc = new TelemetryUploadService(db);

        using var stream = FakeIbtBuilder.Build(laps: 2, customerId: 12345);
        await svc.ProcessAsync(stream, userId, TestContext.Current.CancellationToken);

        Assert.Equal(2, db.UploadedLaps.Count());
        Assert.All(db.UploadedLaps.ToList(), l => Assert.Equal(12345L, l.DriverCustId));
    }

    [Fact]
    public async Task ProcessAsync_CallerWithNoClaimedIdentity_IsAcceptedAndStillRecordsTheDriver()
    {
        await using var db = DbContextFactory.Create();
        var userId = SeedUser(db, claimedCustId: null);
        var svc = new TelemetryUploadService(db);

        using var stream = FakeIbtBuilder.Build(laps: 2, customerId: 999999);
        await svc.ProcessAsync(stream, userId, TestContext.Current.CancellationToken);

        // Nothing to check against, so the upload stands — but the lap still says whose it is.
        Assert.Equal(2, db.UploadedLaps.Count());
        Assert.All(db.UploadedLaps.ToList(), l => Assert.Equal(999999L, l.DriverCustId));
    }

    [Fact]
    public async Task ProcessAsync_FileNamesNoDriver_StoresNullRatherThanCustomerZero()
    {
        await using var db = DbContextFactory.Create();
        var userId = SeedUser(db, claimedCustId: 12345);
        var svc = new TelemetryUploadService(db);

        // DriverUserID absent parses to 0, which is not a Customer ID and must not be compared
        // against the claim — otherwise every such file would be refused.
        using var stream = FakeIbtBuilder.Build(laps: 2, customerId: 0);
        await svc.ProcessAsync(stream, userId, TestContext.Current.CancellationToken);

        Assert.Equal(2, db.UploadedLaps.Count());
        Assert.All(db.UploadedLaps.ToList(), l => Assert.Null(l.DriverCustId));
    }

    [Fact]
    public async Task ProcessAsync_UnknownUserId_IsAcceptedWithNothingToCheckAgainst()
    {
        await using var db = DbContextFactory.Create();
        var svc = new TelemetryUploadService(db);

        // No user row at all: the claim lookup yields null, which must read as "no claim" rather
        // than as a claim of 0 that every file would then fail against.
        using var stream = FakeIbtBuilder.Build(laps: 2, customerId: 999999);
        await svc.ProcessAsync(stream, Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Equal(2, db.UploadedLaps.Count());
    }

    [Fact]
    public async Task ProcessAsync_ValidStream_ReturnsSummaryWithCorrectCounts()
    {
        await using var db = DbContextFactory.Create();
        var svc = new TelemetryUploadService(db);

        using var stream = FakeIbtBuilder.Build(laps: 2, lapTime: 90.5f, validLaps: true);
        var result = await svc.ProcessAsync(stream, Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Equal(2, result.TotalLaps);
        Assert.Equal(2, result.ValidLaps);
        Assert.Equal(90.5, result.BestLapSeconds!.Value, tolerance: 0.005);
        Assert.Equal("Spa-Francorchamps", result.TrackName);
        Assert.Equal("Porsche 992 GT3",   result.CarName);
        Assert.Equal(12345L,              result.CustomerId);
        Assert.Equal("Jerry Holland",     result.DriverName);
    }

    [Fact]
    public async Task ProcessAsync_AbsentConfiguration_StoresAndReturnsEmptyInternalValue()
    {
        await using var db = DbContextFactory.Create();
        var svc = new TelemetryUploadService(db);

        using var stream = FakeIbtBuilder.Build(laps: 1, configName: "N/A");
        var result = await svc.ProcessAsync(stream, Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, result.ConfigName);
        Assert.Equal(string.Empty, db.Tracks.Single().ConfigName);
    }

    [Fact]
    public async Task ProcessAsync_AllInvalidLaps_ReturnsZeroValidAndNullBest()
    {
        await using var db = DbContextFactory.Create();
        var svc = new TelemetryUploadService(db);

        using var stream = FakeIbtBuilder.Build(laps: 2, validLaps: false);
        var result = await svc.ProcessAsync(stream, Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Equal(2, result.TotalLaps);
        Assert.Equal(0, result.ValidLaps);
        Assert.Null(result.BestLapSeconds);
    }

    [Fact]
    public async Task ProcessAsync_CarNotInDb_UpsertsCar()
    {
        await using var db = DbContextFactory.Create();
        var svc = new TelemetryUploadService(db);

        using var stream = FakeIbtBuilder.Build(laps: 1, carId: 99);
        await svc.ProcessAsync(stream, Guid.NewGuid(), TestContext.Current.CancellationToken);

        var car = db.Cars.Single();
        Assert.Equal(99, car.Id);
        Assert.Equal("Porsche 992 GT3", car.Name);
    }

    [Fact]
    public async Task ProcessAsync_CarAlreadyInDb_DoesNotDuplicateCar()
    {
        await using var db = DbContextFactory.Create();
        db.Cars.Add(new Car { Id = 99, Name = "Porsche 992 GT3", NameAbbreviated = "P992" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var svc = new TelemetryUploadService(db);
        using var stream = FakeIbtBuilder.Build(laps: 1, carId: 99);
        await svc.ProcessAsync(stream, Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Single(db.Cars);
    }

    [Fact]
    public async Task ProcessAsync_TimedLaps_SavesEachLapAsUploadedLap()
    {
        await using var db = DbContextFactory.Create();
        var userId = Guid.NewGuid();
        var svc = new TelemetryUploadService(db);

        using var stream = FakeIbtBuilder.Build(laps: 3, lapTime: 95.0f, validLaps: true);
        await svc.ProcessAsync(stream, userId, TestContext.Current.CancellationToken);

        var laps = db.UploadedLaps.ToList();
        Assert.Equal(3, laps.Count);
        Assert.All(laps, l =>
        {
            Assert.Equal(userId, l.UserId);
            Assert.Equal(95.0, l.LapTimeSeconds, tolerance: 0.005);
            Assert.Equal(LapSessionType.Unknown, l.SessionType);
        });
    }

    [Fact]
    public async Task ProcessAsync_ValidLaps_SavesSessionTypeFromFile()
    {
        await using var db = DbContextFactory.Create();
        var userId = Guid.NewGuid();
        var svc = new TelemetryUploadService(db);

        using var stream = FakeIbtBuilder.Build(laps: 1, lapTime: 90.0f, validLaps: true, eventType: LapSessionType.Race);
        await svc.ProcessAsync(stream, userId, TestContext.Current.CancellationToken);

        var lap = Assert.Single(db.UploadedLaps);
        Assert.Equal(LapSessionType.Race, lap.SessionType);
    }

    [Fact]
    public async Task ProcessAsync_InvalidLaps_SavesNoUploadedLaps()
    {
        await using var db = DbContextFactory.Create();
        var svc = new TelemetryUploadService(db);

        using var stream = FakeIbtBuilder.Build(laps: 2, validLaps: false);
        await svc.ProcessAsync(stream, Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Empty(db.UploadedLaps);
    }

    [Fact]
    public async Task ProcessAsync_SameSessionUploadedTwice_DoesNotDuplicateLaps()
    {
        await using var db = DbContextFactory.Create();
        var userId = Guid.NewGuid();
        var svc = new TelemetryUploadService(db);

        // Identical builds share the default sessionDate (0) → same RecordedAt, so the
        // second upload is recognised as the same already-imported session.
        using var first = FakeIbtBuilder.Build(laps: 3, lapTime: 95.0f, validLaps: true);
        await svc.ProcessAsync(first, userId, TestContext.Current.CancellationToken);

        using var second = FakeIbtBuilder.Build(laps: 3, lapTime: 95.0f, validLaps: true);
        await svc.ProcessAsync(second, userId, TestContext.Current.CancellationToken);

        Assert.Equal(3, db.UploadedLaps.Count());
    }

    [Fact]
    public async Task ProcessAsync_SameSessionDifferentUser_InsertsForEachUser()
    {
        await using var db = DbContextFactory.Create();
        var svc = new TelemetryUploadService(db);

        // Deduplication is scoped per user, so two different users uploading the same
        // session each get their own laps.
        using var a = FakeIbtBuilder.Build(laps: 2, lapTime: 90.0f, validLaps: true);
        await svc.ProcessAsync(a, Guid.NewGuid(), TestContext.Current.CancellationToken);

        using var b = FakeIbtBuilder.Build(laps: 2, lapTime: 90.0f, validLaps: true);
        await svc.ProcessAsync(b, Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Equal(4, db.UploadedLaps.Count());
    }
}
