using ApexRacers.Api.Dtos;
using ApexRacers.Core.Models;

namespace ApexRacers.Api.Services;

/// <summary>
/// Pure mapping from the persisted <see cref="Track"/> entity to the catalog DTOs, including
/// image-URL construction from the stored iRacing path bits. Unit-tested directly.
/// </summary>
public static class TrackCatalogMapper
{
    public static TrackCatalogItemDto ToItem(Track track) =>
        new(
            track.Id,
            track.Name,
            track.ConfigName,
            track.Category,
            track.TrackConfigLength,
            track.CornersPerLap,
            track.Location,
            track.NightLighting,
            CatalogImage.FromFolder(track.AssetFolder, track.SmallImageFile));

    public static TrackCatalogDetailDto ToDetail(
        Track track,
        IReadOnlyList<UploadedBestDto> yourBestLaps) =>
        new(
            track.Id,
            track.Name,
            track.ConfigName,
            track.Category,
            track.TrackConfigLength,
            track.CornersPerLap,
            track.Location,
            track.NightLighting,
            track.Latitude,
            track.Longitude,
            track.PitRoadSpeedLimit,
            track.NumberPitstalls,
            track.HasSvgMap,
            CatalogImage.FromFolder(track.AssetFolder, track.SmallImageFile),
            CatalogImage.FromFolder(track.AssetFolder, track.LargeImageFile),
            track.TrackMapUrl,
            yourBestLaps);
}
