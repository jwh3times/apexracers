using System.Text.Json;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using AydskoCar = Aydsko.iRacingData.Cars.CarInfo;
using AydskoCarAsset = Aydsko.iRacingData.Cars.CarAssetDetail;
using AydskoTrack = Aydsko.iRacingData.Tracks.Track;
using AydskoTrackAsset = Aydsko.iRacingData.Tracks.TrackAssets;

namespace ApexRacers.Ingestion;

/// <summary>
/// Pure mapping of the iRacing car/track catalog (+ asset details) onto the persisted
/// <see cref="Car"/>/<see cref="Track"/> entities. Stores raw iRacing path bits (folder + file
/// names); the API composes image URLs at read time. Extracted + unit-tested directly (mirrors
/// <see cref="SubsessionIndexer"/>).
/// </summary>
public static class CatalogIngest
{
    public static void PopulateCar(Car car, AydskoCar info, AydskoCarAsset? asset)
    {
        car.Name = info.CarName;
        car.NameAbbreviated = info.CarNameAbbreviated;
        car.Retired = info.Retired;
        car.FreeWithSubscription = info.FreeWithSubscription;
        car.PackageId = info.PackageId;
        car.Hp = info.Hp;
        car.CarWeight = info.CarWeight;
        car.CarMake = info.CarMake;
        car.CarModel = info.CarModel;
        car.RainEnabled = info.RainEnabled;
        car.CategoriesJson = ToJson(info.Categories);
        car.CarTypesJson = ToJson(info.CarTypes?.Select(t => t.CarType));
        car.AssetFolder = asset?.Folder;
        car.SmallImageFile = asset?.SmallImage;
        car.LargeImageFile = asset?.LargeImage;
        car.LogoPath = asset?.Logo;
    }

    public static void PopulateTrack(Track track, AydskoTrack info, AydskoTrackAsset? asset)
    {
        track.Name = info.TrackName;
        track.ConfigName = ConfigurationName.Normalize(info.ConfigName);
        track.CategoryId = info.CategoryId;
        track.Category = info.Category;
        track.TrackConfigLength = (double)info.TrackConfigLength;
        track.IsDirt = info.IsDirt;
        track.IsOval = info.IsOval;
        track.Location = info.Location;
        track.TimeZone = info.TimeZone;
        track.Retired = info.Retired;
        track.CornersPerLap = info.CornersPerLap;
        track.Latitude = (double)info.Latitude;
        track.Longitude = (double)info.Longitude;
        track.PitRoadSpeedLimit = info.PitRoadSpeedLimit;
        track.NumberPitstalls = info.NumberPitstalls;
        track.NightLighting = info.NightLighting;
        track.HasSvgMap = info.HasSvgMap;
        track.AssetFolder = asset?.Folder;
        track.SmallImageFile = asset?.SmallImage;
        track.LargeImageFile = asset?.LargeImage;
        track.TrackMapUrl = string.IsNullOrEmpty(asset?.TrackMap) ? null : asset.TrackMap;
    }

    private static string? ToJson(IEnumerable<string?>? items)
    {
        var list = items?.Where(s => !string.IsNullOrEmpty(s)).ToList();
        return list is { Count: > 0 } ? JsonSerializer.Serialize(list) : null;
    }
}
