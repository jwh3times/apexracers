using System.Text.Json;
using ApexRacers.Api.Dtos;
using ApexRacers.Core.Models;

namespace ApexRacers.Api.Services;

/// <summary>
/// Pure mapping from the persisted <see cref="Car"/> entity to the catalog DTOs, including
/// image-URL construction from the stored iRacing path bits. Unit-tested directly.
/// </summary>
public static class CarCatalogMapper
{
    public static CarCatalogItemDto ToItem(Car car) =>
        new(
            car.Id,
            car.Name,
            car.NameAbbreviated,
            car.CarMake,
            car.CarModel,
            car.Hp,
            car.CarWeight,
            car.RainEnabled,
            car.FreeWithSubscription ?? false,
            ParseList(car.CategoriesJson),
            CatalogImage.FromFolder(car.AssetFolder, car.SmallImageFile));

    public static CarCatalogDetailDto ToDetail(
        Car car,
        IReadOnlyList<CarClassRefDto> carClasses,
        IReadOnlyList<UploadedBestDto> yourBestLaps) =>
        new(
            car.Id,
            car.Name,
            car.NameAbbreviated,
            car.CarMake,
            car.CarModel,
            car.Hp,
            car.CarWeight,
            car.RainEnabled,
            car.FreeWithSubscription ?? false,
            ParseList(car.CategoriesJson),
            ParseList(car.CarTypesJson),
            CatalogImage.FromFolder(car.AssetFolder, car.SmallImageFile),
            CatalogImage.FromFolder(car.AssetFolder, car.LargeImageFile),
            CatalogImage.FromPath(car.LogoPath),
            carClasses,
            yourBestLaps);

    private static List<string> ParseList(string? json) =>
        string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize<List<string>>(json) ?? [];
}
