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
