using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ApexRacers.Tests.Services;

public class TelemetryUploadServiceTests
{
    private static AppDbContext CreateCatalogDb()
    {
        var db = DbContextFactory.Create();
        db.Cars.Add(new Car { Id = 99, Name = "Porsche 992 GT3", NameAbbreviated = "P992" });
        db.Tracks.Add(new Track { Id = 42, Name = "Spa-Francorchamps", ConfigName = "Full" });
        db.SaveChanges();
        return db;
    }

    [Theory]
    [InlineData(false, true, 1)]
    [InlineData(true, false, 1)]
    [InlineData(false, false, 1)]
    [InlineData(false, false, 0)]
    public async Task ProcessAsync_UnknownCatalogId_RejectsWithoutPersistingAnything(
        bool knownCar, bool knownTrack, int laps)
    {
        await using var db = DbContextFactory.Create();
        var userId = SeedUser(db, claimedCustId: 12345);
        if (knownCar)
            db.Cars.Add(new Car { Id = 99, Name = "Catalog Car", NameAbbreviated = "CAT" });
        if (knownTrack)
            db.Tracks.Add(new Track { Id = 42, Name = "Catalog Track", ConfigName = "Catalog Layout" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        using var stream = FakeIbtBuilder.Build(laps: laps, carName: "Untrusted Car", trackName: "Untrusted Track");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new TelemetryUploadService(db).ProcessAsync(stream, userId, TestContext.Current.CancellationToken));

        Assert.Equal("This telemetry's car or track is not in the catalog yet. Try again after the catalog is updated.", ex.Message);
        Assert.False(db.ChangeTracker.HasChanges());
        Assert.Empty(await db.UploadedLaps.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(knownCar ? 1 : 0, await db.Cars.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(knownTrack ? 1 : 0, await db.Tracks.CountAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProcessAsync_KnownCatalogIds_AcceptsWithoutOverwritingMetadata(bool retired)
    {
        await using var db = CreateCatalogDb();
        var userId = SeedUser(db, claimedCustId: 12345);
        var car = db.Cars.Single();
        car.Retired = retired;
        car.Hp = 500;
        var track = db.Tracks.Single();
        track.Retired = retired;
        track.Location = "Belgium";
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        using var stream = FakeIbtBuilder.Build(laps: 1, carName: "Untrusted Car", carNameShort: "BAD",
            trackName: "Untrusted Track", configName: "Untrusted Layout");

        var result = await new TelemetryUploadService(db).ProcessAsync(stream, userId, TestContext.Current.CancellationToken);

        var savedCar = await db.Cars.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Porsche 992 GT3", savedCar.Name);
        Assert.Equal("P992", savedCar.NameAbbreviated);
        Assert.Equal(retired, savedCar.Retired);
        Assert.Equal(500, savedCar.Hp);
        var savedTrack = await db.Tracks.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Spa-Francorchamps", savedTrack.Name);
        Assert.Equal("Full", savedTrack.ConfigName);
        Assert.Equal(retired, savedTrack.Retired);
        Assert.Equal("Belgium", savedTrack.Location);
        var lap = Assert.Single(await db.UploadedLaps.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(99, lap.CarId);
        Assert.Equal(42, lap.TrackId);
        Assert.Equal(1, result.ValidLaps);
        // The upload summary describes the submitted recording, as before.
        Assert.Equal("Untrusted Car", result.CarName);
        Assert.Equal("Untrusted Track", result.TrackName);
        Assert.Equal("Untrusted Layout", result.ConfigName);
    }

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

        // Driver validation happens before catalog validation and leaves no upload or catalog rows.
        Assert.Empty(db.UploadedLaps);
        Assert.Empty(db.Cars);
        Assert.Empty(db.Tracks);
    }

    [Fact]
    public async Task ProcessAsync_FileMatchesClaimedIdentity_StoresTheRecordingDriver()
    {
        await using var db = CreateCatalogDb();
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
        await using var db = CreateCatalogDb();
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
        await using var db = CreateCatalogDb();
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
        await using var db = CreateCatalogDb();
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
        await using var db = CreateCatalogDb();
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
    public async Task ProcessAsync_AbsentConfiguration_ReturnsEmptyWithoutChangingCatalog()
    {
        await using var db = CreateCatalogDb();
        var svc = new TelemetryUploadService(db);

        using var stream = FakeIbtBuilder.Build(laps: 1, configName: "N/A");
        var result = await svc.ProcessAsync(stream, Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, result.ConfigName);
        Assert.Equal("Full", db.Tracks.Single().ConfigName);
    }

    [Fact]
    public async Task ProcessAsync_AllInvalidLaps_ReturnsZeroValidAndNullBest()
    {
        await using var db = CreateCatalogDb();
        var svc = new TelemetryUploadService(db);

        using var stream = FakeIbtBuilder.Build(laps: 2, validLaps: false);
        var result = await svc.ProcessAsync(stream, Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Equal(2, result.TotalLaps);
        Assert.Equal(0, result.ValidLaps);
        Assert.Null(result.BestLapSeconds);
    }

    [Fact]
    public async Task ProcessAsync_CarAlreadyInDb_DoesNotDuplicateCar()
    {
        await using var db = CreateCatalogDb();

        var svc = new TelemetryUploadService(db);
        using var stream = FakeIbtBuilder.Build(laps: 1, carId: 99);
        await svc.ProcessAsync(stream, Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Single(db.Cars);
    }

    [Fact]
    public async Task ProcessAsync_TimedLaps_SavesEachLapAsUploadedLap()
    {
        await using var db = CreateCatalogDb();
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
        await using var db = CreateCatalogDb();
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
        await using var db = CreateCatalogDb();
        var svc = new TelemetryUploadService(db);

        using var stream = FakeIbtBuilder.Build(laps: 2, validLaps: false);
        await svc.ProcessAsync(stream, Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Empty(db.UploadedLaps);
    }

    [Fact]
    public async Task ProcessAsync_SameSessionUploadedTwice_DoesNotDuplicateLaps()
    {
        await using var db = CreateCatalogDb();
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
        await using var db = CreateCatalogDb();
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
