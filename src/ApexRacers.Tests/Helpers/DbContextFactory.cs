using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Tests.Helpers;

public static class DbContextFactory
{
    public static AppDbContext Create()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }
}
