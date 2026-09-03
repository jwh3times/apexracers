using ApexRacers.Core.Models;
using ApexRacers.Data.Migrations;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace ApexRacers.Tests.Data;

[Collection(PostgreSqlCollection.Name)]
public class ConfigurationNameMigrationTests(PostgreSqlFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Up_NormalizesOnlyAbsentConfigurationSpellings()
    {
        await using var db = await postgres.CreateDbContextAsync(Ct);
        db.Tracks.AddRange(
            Track(1, ""),
            Track(2, "   "),
            Track(3, "N/A"),
            Track(4, " n/a "),
            Track(5, "Grand Prix"));
        await db.SaveChangesAsync(Ct);

        var migration = new NormalizeTrackConfigurationNames();
        var operation = Assert.IsType<SqlOperation>(Assert.Single(migration.UpOperations));
        await db.Database.ExecuteSqlRawAsync(operation.Sql, Ct);
        db.ChangeTracker.Clear();

        var values = await db.Tracks
            .OrderBy(track => track.Id)
            .Select(track => track.ConfigName)
            .ToListAsync(Ct);
        Assert.Equal(["", "", "", "", "Grand Prix"], values);
        Assert.Empty(migration.DownOperations);
    }

    private static Track Track(int id, string configName) =>
        new() { Id = id, Name = $"Track {id}", ConfigName = configName };
}
