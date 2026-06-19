using ApexRacers.Api.Dtos;
using ApexRacers.Data;
using Aydsko.iRacingData.Cars;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

/// <summary>
/// Browsable car catalog over the live iRacing API via <see cref="CachedIRacingClient"/> (24 h TTL).
/// Detail joins car-class membership from the local catalog and overlays the caller's personal best
/// laps in that car when a user id is supplied.
/// </summary>
public class CarCatalogService(CachedIRacingClient cached, AppDbContext db)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    public async Task<IReadOnlyList<CarCatalogItemDto>> ListAsync(CancellationToken ct)
    {
        var cars = await CarsAsync(ct);
        var assets = await AssetsAsync(ct);
        return cars
            .Select(c => CarCatalogMapper.ToItem(c, assets.GetValueOrDefault(c.CarId.ToString())))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<CarCatalogDetailDto> GetAsync(int carId, Guid? userId, CancellationToken ct)
    {
        var cars = await CarsAsync(ct);
        var car = cars.FirstOrDefault(c => c.CarId == carId)
            ?? throw new KeyNotFoundException($"Car {carId} was not found in the catalog.");
        var assets = await AssetsAsync(ct);

        var carClasses = await db.CarClassCars
            .Where(x => x.CarId == carId)
            .Join(db.CarClasses, x => x.CarClassId, cc => cc.Id, (_, cc) => new CarClassRefDto(cc.Id, cc.Name))
            .ToListAsync(ct);

        IReadOnlyList<PersonalLapDto> bests = userId is { } uid
            ? await PersonalBestsForCarAsync(uid, carId, ct)
            : [];

        return CarCatalogMapper.ToDetail(car, assets.GetValueOrDefault(carId.ToString()), carClasses, bests);
    }

    private Task<List<CarInfo>> CarsAsync(CancellationToken ct) =>
        cached.GetOrFetchAsync("catalog:cars", Ttl,
            async c => (await c.GetCarsAsync(ct)).Data.ToList(), ct);

    private Task<Dictionary<string, CarAssetDetail>> AssetsAsync(CancellationToken ct) =>
        cached.GetOrFetchAsync("catalog:car-assets", Ttl,
            async c => (await c.GetCarAssetDetailsAsync(ct)).Data.ToDictionary(k => k.Key, v => v.Value), ct);

    private async Task<List<PersonalLapDto>> PersonalBestsForCarAsync(
        Guid userId, int carId, CancellationToken ct)
    {
        var rows = await db.PersonalLaps
            .Where(l => l.UserId == userId && l.CarId == carId && l.IsValidLap)
            .Select(l => new
            {
                l.CarId,
                CarName = l.Car.Name,
                TrackName = l.Track.Name,
                ConfigName = l.Track.ConfigName,
                l.LapTimeSeconds,
                l.RecordedAt,
            })
            .ToListAsync(ct);

        return rows
            .GroupBy(l => new { l.CarId, l.CarName, l.TrackName, l.ConfigName })
            .Select(g => new PersonalLapDto(
                g.Key.CarId, g.Key.CarName, g.Key.TrackName, g.Key.ConfigName,
                g.Min(l => l.LapTimeSeconds), g.Count(), g.Max(l => l.RecordedAt)))
            .OrderBy(d => d.BestLapSeconds)
            .ToList();
    }
}
