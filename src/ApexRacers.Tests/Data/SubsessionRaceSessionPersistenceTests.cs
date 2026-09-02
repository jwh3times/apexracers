using ApexRacers.Core.Models;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ApexRacers.Tests.Data;

public class SubsessionRaceSessionPersistenceTests
{
    [Fact]
    public async Task RaceSessionId_PersistsAndGroupsSiblingSplits_WhileAllowingLegacyNulls()
    {
        await using var db = DbContextFactory.Create();
        db.Subsessions.AddRange(
            Subsession(86109311, raceSessionId: 311244815),
            Subsession(86109312, raceSessionId: 311244815),
            Subsession(86109400, raceSessionId: 311244900),
            Subsession(86100000, raceSessionId: null));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var siblingSplitIds = await db.Subsessions
            .Where(s => s.RaceSessionId == 311244815)
            .OrderBy(s => s.Id)
            .Select(s => s.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal([86109311, 86109312], siblingSplitIds);
        Assert.Null(await db.Subsessions
            .Where(s => s.Id == 86100000)
            .Select(s => s.RaceSessionId)
            .SingleAsync(TestContext.Current.CancellationToken));
    }

    private static Subsession Subsession(int id, int? raceSessionId) => new()
    {
        Id = id,
        RaceSessionId = raceSessionId,
    };
}
