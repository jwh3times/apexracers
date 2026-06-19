using ApexRacers.Api.Dtos;
using Aydsko.iRacingData.Cars;

namespace ApexRacers.Api.Services;

/// <summary>
/// Pure mapping from the iRacing car catalog (<see cref="CarInfo"/> + <see cref="CarAssetDetail"/>)
/// to the catalog DTOs, including image-URL construction. Extracted as a pure helper so the mapping
/// and URL building are unit-tested directly (mirrors <c>LeaderboardCsvParser</c>).
/// </summary>
public static class CarCatalogMapper
{
    public static CarCatalogItemDto ToItem(CarInfo car, CarAssetDetail? asset) =>
        new(
            car.CarId,
            car.CarName,
            car.CarNameAbbreviated,
            car.CarMake,
            car.CarModel,
            car.Hp,
            car.CarWeight,
            car.RainEnabled,
            car.FreeWithSubscription,
            car.Categories?.ToList() ?? [],
            CatalogImage.FromFolder(asset?.Folder, asset?.SmallImage));

    public static CarCatalogDetailDto ToDetail(
        CarInfo car,
        CarAssetDetail? asset,
        IReadOnlyList<CarClassRefDto> carClasses,
        IReadOnlyList<PersonalLapDto> yourBestLaps) =>
        new(
            car.CarId,
            car.CarName,
            car.CarNameAbbreviated,
            car.CarMake,
            car.CarModel,
            car.Hp,
            car.CarWeight,
            car.RainEnabled,
            car.FreeWithSubscription,
            car.Categories?.ToList() ?? [],
            (car.CarTypes ?? [])
                .Select(t => t.CarType)
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList(),
            CatalogImage.FromFolder(asset?.Folder, asset?.SmallImage),
            CatalogImage.FromFolder(asset?.Folder, asset?.LargeImage),
            CatalogImage.FromPath(asset?.Logo),
            carClasses,
            yourBestLaps);
}
