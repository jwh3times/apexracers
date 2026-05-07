using ApexRacers.Api.Dtos;
using ApexRacers.Data;

namespace ApexRacers.Api.Services;

public class SeriesService(AppDbContext db)
{
    // TODO: Query db.Series, filter to active series, map to SeriesDto list
    public Task<List<SeriesDto>> GetActiveSeriesAsync(CancellationToken ct = default)
        => throw new NotImplementedException();
}
