using ApexRacers.Core.Models;
using Xunit;

namespace ApexRacers.Tests.Models;

public class CoreModelTests
{
    [Fact]
    public void CarClassCar_Properties_RoundTrip()
    {
        var carClass = new CarClass { Id = 1, Name = "GT3", ShortName = "GT3", RelativeSpeed = 100 };
        var car = new Car { Id = 2, Name = "Ferrari 296", NameAbbreviated = "F296" };
        var join = new CarClassCar { CarClassId = 1, CarId = 2, CarClass = carClass, Car = car };

        Assert.Equal(1, join.CarClassId);
        Assert.Equal(2, join.CarId);
        Assert.Same(carClass, join.CarClass);
        Assert.Same(car, join.Car);
    }

    [Fact]
    public void SeasonCar_Properties_RoundTrip()
    {
        var series = new Series { Id = 1, Name = "GT3 Cup" };
        var season = new Season { Id = 1, SeriesId = 1, Year = 2026, Quarter = 2, Active = true, Series = series };
        var car = new Car { Id = 3, Name = "Porsche 992", NameAbbreviated = "P992" };
        var join = new SeasonCar { SeasonId = 1, CarId = 3, Season = season, Car = car };

        Assert.Equal(1, join.SeasonId);
        Assert.Equal(3, join.CarId);
        Assert.Same(season, join.Season);
        Assert.Same(car, join.Car);
    }

    [Fact]
    public void SeasonCarClass_Properties_RoundTrip()
    {
        var series = new Series { Id = 1, Name = "GT3 Cup" };
        var season = new Season { Id = 1, SeriesId = 1, Year = 2026, Quarter = 2, Active = true, Series = series };
        var carClass = new CarClass { Id = 5, Name = "GT3", ShortName = "GT3", RelativeSpeed = 100 };
        var join = new SeasonCarClass { SeasonId = 1, CarClassId = 5, Season = season, CarClass = carClass };

        Assert.Equal(1, join.SeasonId);
        Assert.Equal(5, join.CarClassId);
        Assert.Same(season, join.Season);
        Assert.Same(carClass, join.CarClass);
    }

    [Fact]
    public void LapSessionType_EnumValues_AreCorrect()
    {
        Assert.Equal(0, (byte)LapSessionType.Unknown);
        Assert.Equal(1, (byte)LapSessionType.Practice);
        Assert.Equal(2, (byte)LapSessionType.Qualifying);
        Assert.Equal(3, (byte)LapSessionType.TimeTrial);
        Assert.Equal(4, (byte)LapSessionType.Race);
        Assert.Equal(5, (byte)LapSessionType.LoneQualify);
    }

    [Fact]
    public void Car_OptionalProperties_RoundTrip()
    {
        var car = new Car
        {
            Id = 42,
            Name = "Porsche 992 GT3",
            NameAbbreviated = "P992",
            Retired = true,
            FreeWithSubscription = false,
            PackageId = 100,
            Hp = 500,
            CarWeight = 1200,
        };

        Assert.Equal(42, car.Id);
        Assert.True(car.Retired);
        Assert.False(car.FreeWithSubscription);
        Assert.Equal(100, car.PackageId);
        Assert.Equal(500, car.Hp);
        Assert.Equal(1200, car.CarWeight);
    }

    [Fact]
    public void Track_OptionalProperties_RoundTrip()
    {
        var track = new Track
        {
            Id = 1,
            Name = "Sebring",
            ConfigName = "12-hour",
            CategoryId = 2,
            Category = "road",
            TrackConfigLength = 3.74,
            IsDirt = false,
            IsOval = false,
            Location = "Sebring, FL",
            TimeZone = "America/New_York",
            Retired = false,
        };

        Assert.Equal(2, track.CategoryId);
        Assert.Equal("road", track.Category);
        Assert.Equal(3.74, track.TrackConfigLength);
        Assert.Equal("Sebring, FL", track.Location);
        Assert.Equal("America/New_York", track.TimeZone);
        Assert.False(track.Retired);
    }

    [Fact]
    public void Series_OptionalProperties_RoundTrip()
    {
        var series = new Series
        {
            Id = 1,
            Name = "GT3 Cup",
            CategoryId = 2,
            Category = "road",
            LicenseGroup = 4,
            Official = true,
        };

        Assert.Equal(2, series.CategoryId);
        Assert.Equal("road", series.Category);
        Assert.Equal(4, series.LicenseGroup);
        Assert.True(series.Official);
    }

    [Fact]
    public void Season_OptionalProperties_RoundTrip()
    {
        var series = new Series { Id = 1, Name = "GT3 Cup" };
        var season = new Season
        {
            Id = 1,
            SeriesId = 1,
            Year = 2026,
            Quarter = 2,
            Active = true,
            LicenseGroup = 3,
            Official = true,
            Drops = 2,
            FixedSetup = false,
            Multiclass = true,
            Series = series,
        };

        Assert.Equal(3, season.LicenseGroup);
        Assert.True(season.Official);
        Assert.Equal(2, season.Drops);
        Assert.False(season.FixedSetup);
        Assert.True(season.Multiclass);
    }

    [Fact]
    public void PersonalLap_SessionType_DefaultIsUnknown()
    {
        var lap = new PersonalLap();
        Assert.Equal(LapSessionType.Unknown, lap.SessionType);
    }

    [Fact]
    public void PersonalLap_SessionType_CanBeSetToRace()
    {
        var lap = new PersonalLap { SessionType = LapSessionType.Race };
        Assert.Equal(LapSessionType.Race, lap.SessionType);
    }
}
