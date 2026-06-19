using ApexRacers.Api.Dtos;
using ApexRacers.Api.Services;
using Aydsko.iRacingData.Tracks;
using Xunit;

namespace ApexRacers.Tests.Services;

public class TrackCatalogMapperTests
{
    private static Track Track() => new()
    {
        TrackId = 18,
        TrackName = "Lime Rock Park",
        ConfigName = "Grand Prix",
        Category = "road",
        TrackConfigLength = 1.53m,
        CornersPerLap = 7,
        Location = "Lakeville, Connecticut, USA",
        NightLighting = false,
        Latitude = 41.9298m,
        Longitude = -73.3839m,
        PitRoadSpeedLimit = 45,
        NumberPitstalls = 30,
        HasSvgMap = true,
    };

    private static TrackAssets Asset() => new()
    {
        TrackId = 18,
        Folder = "/img/tracks/limerockpark",
        SmallImage = "limerockpark-small.jpg",
        LargeImage = "limerockpark-large.jpg",
        TrackMap = "https://members-assets.iracing.com/public/track-maps/tracks_limerock/1-limerock-full/",
    };

    [Fact]
    public void ToItem_MapsCoreFieldsAndLength()
    {
        var item = TrackCatalogMapper.ToItem(Track(), Asset());

        Assert.Equal(18, item.TrackId);
        Assert.Equal("Lime Rock Park", item.Name);
        Assert.Equal("Grand Prix", item.ConfigName);
        Assert.Equal("road", item.Category);
        Assert.Equal(1.53, item.LengthMiles!.Value, precision: 3);
        Assert.Equal(7, item.CornersPerLap);
        Assert.Equal("Lakeville, Connecticut, USA", item.Location);
        Assert.False(item.NightLighting);
        Assert.Equal(
            "https://images-static.iracing.com/img/tracks/limerockpark/limerockpark-small.jpg",
            item.SmallImageUrl);
    }

    [Fact]
    public void ToItem_NullAsset_NullImage()
    {
        var item = TrackCatalogMapper.ToItem(Track(), null);
        Assert.Null(item.SmallImageUrl);
        Assert.Equal("Lime Rock Park", item.Name);
    }

    [Fact]
    public void ToDetail_MapsGeoPitImagesMapAndBests()
    {
        var bests = new List<PersonalLapDto>
        {
            new(132, "Merc GT3", "Lime Rock Park", "Grand Prix", 48.9, 5, DateTimeOffset.UtcNow),
        };

        var detail = TrackCatalogMapper.ToDetail(Track(), Asset(), bests);

        Assert.Equal(41.9298, detail.Latitude!.Value, precision: 4);
        Assert.Equal(-73.3839, detail.Longitude!.Value, precision: 4);
        Assert.Equal(45, detail.PitRoadSpeedLimit);
        Assert.Equal(30, detail.NumberPitstalls);
        Assert.True(detail.HasSvgMap);
        Assert.Equal(
            "https://images-static.iracing.com/img/tracks/limerockpark/limerockpark-large.jpg",
            detail.LargeImageUrl);
        Assert.Equal(
            "https://members-assets.iracing.com/public/track-maps/tracks_limerock/1-limerock-full/",
            detail.TrackMapUrl);
        Assert.Equal("Lime Rock Park", Assert.Single(detail.YourBestLaps).TrackName);
    }

    [Fact]
    public void ToDetail_NullAsset_NullImagesAndMap()
    {
        var detail = TrackCatalogMapper.ToDetail(Track(), null, []);
        Assert.Null(detail.LargeImageUrl);
        Assert.Null(detail.TrackMapUrl);
    }
}
