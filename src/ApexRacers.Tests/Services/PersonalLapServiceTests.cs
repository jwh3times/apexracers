using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Tests.Helpers;
using Xunit;

namespace ApexRacers.Tests.Services;

public class PersonalLapServiceTests
{
    private static (UserProfile user, Car car) SeedUserAndCar(ApexRacers.Data.AppDbContext db)
    {
        var user = new UserProfile { Id = Guid.NewGuid(), IRacingCustomerId = 1, DisplayName = "Jerry" };
        var car = new Car { Id = 1, Name = "Porsche 992 GT3", NameAbbreviated = "P992" };
        db.UserProfiles.Add(user);
        db.Cars.Add(car);
        return (user, car);
    }

    private static PersonalLap MakeLap(UserProfile user, Car car, double lapTime, bool isValid = true, int daysAgo = 0) =>
        new()
        {
            UserId = user.Id,
            CarId = car.Id,
            TrackName = "Spa",
            ConfigName = "Full",
            LapTimeSeconds = lapTime,
            IsValidLap = isValid,
            RecordedAt = DateTimeOffset.UtcNow.AddDays(-daysAgo),
            IracingTrackId = 1,
            User = user,
            Car = car,
        };

    [Fact]
    public async Task GetPersonalBestsAsync_NoLaps_ReturnsEmpty()
    {
        await using var db = DbContextFactory.Create();
        SeedUserAndCar(db);
        await db.SaveChangesAsync();

        var result = await new PersonalLapService(db).GetPersonalBestsAsync(customerId: 1);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetPersonalBestsAsync_OnlyInvalidLaps_ReturnsEmpty()
    {
        await using var db = DbContextFactory.Create();
        var (user, car) = SeedUserAndCar(db);
        db.PersonalLaps.Add(MakeLap(user, car, lapTime: 70, isValid: false));
        await db.SaveChangesAsync();

        var result = await new PersonalLapService(db).GetPersonalBestsAsync(customerId: 1);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetPersonalBestsAsync_MultipleLapsSameCarTrack_ReturnsBestLapAndCount()
    {
        await using var db = DbContextFactory.Create();
        var (user, car) = SeedUserAndCar(db);
        db.PersonalLaps.AddRange(
            MakeLap(user, car, lapTime: 70),
            MakeLap(user, car, lapTime: 60),
            MakeLap(user, car, lapTime: 80));
        await db.SaveChangesAsync();

        var result = await new PersonalLapService(db).GetPersonalBestsAsync(customerId: 1);

        var dto = Assert.Single(result);
        Assert.Equal(60, dto.BestLapSeconds);
        Assert.Equal(3, dto.LapCount);
    }

    [Fact]
    public async Task GetPersonalBestsAsync_ValidAndInvalidMixed_CountsOnlyValidLaps()
    {
        await using var db = DbContextFactory.Create();
        var (user, car) = SeedUserAndCar(db);
        db.PersonalLaps.AddRange(
            MakeLap(user, car, lapTime: 70, isValid: true),
            MakeLap(user, car, lapTime: 60, isValid: false), // invalid — should not count
            MakeLap(user, car, lapTime: 80, isValid: true));
        await db.SaveChangesAsync();

        var result = await new PersonalLapService(db).GetPersonalBestsAsync(customerId: 1);

        var dto = Assert.Single(result);
        Assert.Equal(2, dto.LapCount);
        Assert.Equal(70, dto.BestLapSeconds); // 60 was invalid
    }

    [Fact]
    public async Task GetPersonalBestsAsync_TwoDifferentCarTrackCombos_OrderedByMostRecentFirst()
    {
        await using var db = DbContextFactory.Create();
        var (user, car) = SeedUserAndCar(db);
        var car2 = new Car { Id = 2, Name = "Ferrari 296 GT3", NameAbbreviated = "F296" };
        db.Cars.Add(car2);
        db.PersonalLaps.AddRange(
            MakeLap(user, car, lapTime: 70, daysAgo: 7),
            new PersonalLap
            {
                UserId = user.Id, CarId = 2, TrackName = "Monza", ConfigName = "Full",
                LapTimeSeconds = 55, IsValidLap = true,
                RecordedAt = DateTimeOffset.UtcNow.AddDays(-1),
                IracingTrackId = 2, User = user, Car = car2,
            });
        await db.SaveChangesAsync();

        var result = await new PersonalLapService(db).GetPersonalBestsAsync(customerId: 1);

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result[0].CarId); // Ferrari lap recorded 1 day ago
        Assert.Equal(1, result[1].CarId);
    }
}
