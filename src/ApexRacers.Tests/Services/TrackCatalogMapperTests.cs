using ApexRacers.Api.Dtos;
using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using Xunit;

namespace ApexRacers.Tests.Services;

public class TrackCatalogMapperTests
{
    private static Track SampleTrack() => new()
    {
        Id = 18,
        Name = "Lime Rock Park",
        ConfigName = "Grand Prix",
        Category = "road",
        TrackConfigLength = 1.53,
        CornersPerLap = 7,
        Location = "Lakeville, Connecticut, USA",
        NightLighting = false,
        Latitude = 41.9298,
        Longitude = -73.3839,
        PitRoadSpeedLimit = 45,
        NumberPitstalls = 30,
        HasSvgMap = true,
        AssetFolder = "/img/tracks/limerockpark",
        SmallImageFile = "limerockpark-small.jpg",
        LargeImageFile = "limerockpark-large.jpg",
        TrackMapUrl = "https://members-assets.iracing.com/public/track-maps/tracks_limerock/1-limerock-full/",
    };

    [Fact]
    public void ToItem_MapsCoreFieldsAndImage()
    {
        var item = TrackCatalogMapper.ToItem(SampleTrack());

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
    public void ToItem_NoAssetFolder_NullImage()
    {
        var track = SampleTrack();
        track.AssetFolder = null;
        track.SmallImageFile = null;

        var item = TrackCatalogMapper.ToItem(track);

        Assert.Null(item.SmallImageUrl);
        Assert.Equal("Lime Rock Park", item.Name);
    }

    [Fact]
    public void CatalogDtos_AbsentConfiguration_ReportNull()
    {
        var track = SampleTrack();
        track.ConfigName = string.Empty;

        Assert.Null(TrackCatalogMapper.ToItem(track).ConfigName);
        Assert.Null(TrackCatalogMapper.ToDetail(track, []).ConfigName);
    }

    [Fact]
    public void ToDetail_MapsGeoPitImagesMapAndBests()
    {
        var bests = new List<UploadedBestDto>
        {
            new(132, "Merc GT3", 149, "Lime Rock Park", "Grand Prix", 48.9, 5, DateTimeOffset.UtcNow),
        };

        var detail = TrackCatalogMapper.ToDetail(SampleTrack(), bests);

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
        Assert.Equal("Lime Rock Park", Assert.Single(detail.YourUploadedBests).TrackName);
    }
}
