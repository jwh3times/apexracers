using ApexRacers.Api.Dtos;
using ApexRacers.Data;

namespace ApexRacers.Api.Services;

public class PersonalLapService(AppDbContext db)
{
    public Task<List<PersonalLapDto>> GetPersonalBestsAsync(Guid userId, CancellationToken ct = default) =>
        PersonalBestQuery.RunAsync(
            db.PersonalLaps.Where(l => l.UserId == userId),
            PersonalBestOrder.MostRecentFirst,
            ct);
}
