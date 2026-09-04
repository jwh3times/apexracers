using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Data.Migrations;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace ApexRacers.Tests.Data;

[Collection(PostgreSqlCollection.Name)]
public class RaceWeekIndexMigrationTests(PostgreSqlFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Up_RenamesPersistedColumnsWithoutChangingValuesOrRelationships()
    {
        await using var db = await postgres.CreateDbContextAsync(Ct);
        var weekId = Guid.NewGuid();
        db.Series.Add(new Series { Id = 10, Name = "Series" });
        db.Seasons.Add(new Season { Id = 20, SeriesId = 10 });
        db.Tracks.Add(new Track { Id = 30, Name = "Track" });
        db.Weeks.Add(new Week
        {
            Id = weekId,
            SeasonId = 20,
            RaceWeekIndex = 6,
            TrackId = 30,
            StartDate = new DateOnly(2026, 9, 1),
        });
        db.Subsessions.Add(new Subsession
        {
            Id = 40,
            SeasonId = 20,
            RaceWeekIndex = 6,
            WeekId = weekId,
            TrackId = 30,
            StartTime = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
        });
        db.SeasonCarBops.Add(new SeasonCarBop
        {
            SeasonId = 20,
            RaceWeekIndex = 6,
            CarId = 50,
        });
        await db.SaveChangesAsync(Ct);

        var migration = new RenameWeekNumberToRaceWeekIndex();
        await ApplyAsync(db, migration.DownOperations);
        Assert.Equal(
            ["SeasonCarBops.WeekNumber", "Subsessions.WeekNumber", "Weeks.WeekNumber"],
            await RaceWeekColumnsAsync(db));
        Assert.Equal(
            [
                "SeasonCarBops.IX_SeasonCarBops_SeasonId_WeekNumber",
                "Subsessions.IX_Subsessions_SeasonId_WeekNumber",
                "Weeks.IX_Weeks_SeasonId_WeekNumber",
            ],
            await RaceWeekIndexesAsync(db));

        await ApplyAsync(db, migration.UpOperations);
        db.ChangeTracker.Clear();

        Assert.Equal(
            ["SeasonCarBops.RaceWeekIndex", "Subsessions.RaceWeekIndex", "Weeks.RaceWeekIndex"],
            await RaceWeekColumnsAsync(db));
        Assert.Equal(
            [
                "SeasonCarBops.IX_SeasonCarBops_SeasonId_RaceWeekIndex",
                "Subsessions.IX_Subsessions_SeasonId_RaceWeekIndex",
                "Weeks.IX_Weeks_SeasonId_RaceWeekIndex",
            ],
            await RaceWeekIndexesAsync(db));

        var week = await db.Weeks.Include(row => row.Subsessions).SingleAsync(Ct);
        Assert.Equal(6, week.RaceWeekIndex);
        var subsession = Assert.Single(week.Subsessions);
        Assert.Equal(6, subsession.RaceWeekIndex);
        Assert.Equal(week.Id, subsession.WeekId);
        Assert.Equal(6, (await db.SeasonCarBops.SingleAsync(Ct)).RaceWeekIndex);
    }

    private static async Task ApplyAsync(
        AppDbContext db, IReadOnlyList<MigrationOperation> operations)
    {
        var generator = db.GetService<IMigrationsSqlGenerator>();
        foreach (var command in generator.Generate(operations, db.Model))
            await db.Database.ExecuteSqlRawAsync(command.CommandText, Ct);
    }

    private static async Task<List<string>> RaceWeekColumnsAsync(AppDbContext db) =>
        await db.Database.SqlQueryRaw<string>(
                """
                SELECT "table_name" || '.' || "column_name" AS "Value"
                FROM information_schema.columns
                WHERE table_schema = 'iracing'
                  AND table_name IN ('Weeks', 'Subsessions', 'SeasonCarBops')
                  AND column_name IN ('WeekNumber', 'RaceWeekIndex')
                ORDER BY "table_name"
                """)
            .ToListAsync(Ct);

    private static async Task<List<string>> RaceWeekIndexesAsync(AppDbContext db) =>
        await db.Database.SqlQueryRaw<string>(
                """
                SELECT tablename || '.' || indexname AS "Value"
                FROM pg_indexes
                WHERE schemaname = 'iracing'
                  AND tablename IN ('Weeks', 'Subsessions', 'SeasonCarBops')
                  AND indexname IN (
                      'IX_Weeks_SeasonId_WeekNumber',
                      'IX_Weeks_SeasonId_RaceWeekIndex',
                      'IX_Subsessions_SeasonId_WeekNumber',
                      'IX_Subsessions_SeasonId_RaceWeekIndex',
                      'IX_SeasonCarBops_SeasonId_WeekNumber',
                      'IX_SeasonCarBops_SeasonId_RaceWeekIndex')
                ORDER BY tablename
                """)
            .ToListAsync(Ct);
}
