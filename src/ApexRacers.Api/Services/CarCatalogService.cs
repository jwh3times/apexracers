using ApexRacers.Api.Dtos;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

/// <summary>
/// Browsable car catalog, read from the persisted <see cref="Core.Models.Car"/> catalog (populated
/// by the ingestion worker + seeder). Detail joins car-class membership and overlays the caller's
/// public catalog metadata. Private upload overlays remain unavailable until their
/// ownership and protected publication workflow joins Driver Authorization.
/// </summary>
public class CarCatalogService(AppDbContext db)
{
    public async Task<IReadOnlyList<CarCatalogItemDto>> ListAsync(CancellationToken ct)
    {
        var cars = await db.Cars
            .AsNoTracking()
            .Where(c => c.Retired != true)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);
        return cars.Select(CarCatalogMapper.ToItem).ToList();
    }

    public async Task<CarCatalogDetailDto> GetAsync(int carId, Guid? userId, CancellationToken ct)
    {
        var car = await db.Cars.AsNoTracking().FirstOrDefaultAsync(c => c.Id == carId, ct)
            ?? throw new KeyNotFoundException($"Car {carId} was not found in the catalog.");

        var carClasses = await db.CarClassCars
            .Where(x => x.CarId == carId)
            .Join(db.CarClasses, x => x.CarClassId, cc => cc.Id, (_, cc) => new CarClassRefDto(cc.Id, cc.Name))
            .ToListAsync(ct);

        return CarCatalogMapper.ToDetail(car, carClasses, []);
    }
}
