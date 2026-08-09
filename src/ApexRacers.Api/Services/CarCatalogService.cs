using ApexRacers.Api.Dtos;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

/// <summary>
/// Browsable car catalog, read from the persisted <see cref="Core.Models.Car"/> catalog (populated
/// by the ingestion worker + seeder). Detail joins car-class membership and overlays the caller's
/// personal best laps in that car when a user id is supplied.
/// </summary>
public class CarCatalogService(AppDbContext db)
{
    public async Task<IReadOnlyList<CarCatalogItemDto>> ListAsync(CancellationToken ct)
    {
        var cars = await db.Cars
            .AsNoTracking()
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

        IReadOnlyList<PersonalLapDto> bests = userId is { } uid
            ? await PersonalBestsForCarAsync(uid, carId, ct)
            : [];

        return CarCatalogMapper.ToDetail(car, carClasses, bests);
    }

    private Task<List<PersonalLapDto>> PersonalBestsForCarAsync(
        Guid userId, int carId, CancellationToken ct) =>
        PersonalBestQuery.RunAsync(
            db.PersonalLaps.Where(l => l.UserId == userId && l.CarId == carId),
            PersonalBestOrder.FastestFirst,
            ct);
}
