using ApexRacers.Api.Dtos;
using Aydsko.iRacingData.Tracks;

namespace ApexRacers.Api.Services;

/// <summary>
/// Pure mapping from the iRacing track catalog (<see cref="Track"/> + <see cref="TrackAssets"/>)
/// to the catalog DTOs, including image-URL construction. Unit-tested directly (mirrors
/// <c>CarCatalogMapper</c>).
/// </summary>
public static class TrackCatalogMapper
{
    public static TrackCatalogItemDto ToItem(Track track, TrackAssets? asset) =>
        new(
            track.TrackId,
            track.TrackName,
            track.ConfigName,
            track.Category,
            (double)track.TrackConfigLength,
            track.CornersPerLap,
            track.Location,
            track.NightLighting,
            CatalogImage.FromFolder(asset?.Folder, asset?.SmallImage));

    public static TrackCatalogDetailDto ToDetail(
        Track track,
        TrackAssets? asset,
        IReadOnlyList<PersonalLapDto> yourBestLaps) =>
        new(
            track.TrackId,
            track.TrackName,
            track.ConfigName,
            track.Category,
            (double)track.TrackConfigLength,
            track.CornersPerLap,
            track.Location,
            track.NightLighting,
            (double)track.Latitude,
            (double)track.Longitude,
            track.PitRoadSpeedLimit,
            track.NumberPitstalls,
            track.HasSvgMap,
            CatalogImage.FromFolder(asset?.Folder, asset?.SmallImage),
            CatalogImage.FromFolder(asset?.Folder, asset?.LargeImage),
            string.IsNullOrEmpty(asset?.TrackMap) ? null : asset.TrackMap,
            yourBestLaps);
}
