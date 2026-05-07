using ApexRacers.Api.Dtos;
using ApexRacers.Data;

namespace ApexRacers.Api.Services;

public class WeekCarStatsService(AppDbContext db)
{
    // TODO: Validate that seriesId/weekId exist; group LapTimeEntries by CarId for the week;
    //       compute entry count, fastest lap (MIN), and median lap per car; map to WeekCarDto list
    public Task<List<WeekCarDto>> GetCarsForWeekAsync(int seriesId, int weekId, CancellationToken ct = default)
        => throw new NotImplementedException();
}
