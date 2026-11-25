using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Npgsql;
using Xunit;
using Xunit.v3;

namespace ApexRacers.Tests.Helpers;

/// <summary>
/// Issue #568: the shared PostgreSQL container used to keep every test's database (about 9 GB by
/// the end of a full run), and removing it at the end of the run outlasted Docker.DotNet's 100 s
/// request timeout, which failed every test in the collection. Databases left by finished tests
/// are dropped before the next test in the collection starts, so the container stays small.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class FinishedTestDatabasesTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task DropsTheDatabasesThatExistedWhenTheLastTestFinishedAndKeepsLaterOnes()
    {
        var ct = TestContext.Current.CancellationToken;
        var finished = await DatabaseNameAsync(await fixture.CreateOptionsAsync(ct));
        var cleanup = new FinishedTestDatabases(fixture.Container.GetConnectionString());

        cleanup.RecordFinished();
        var current = await DatabaseNameAsync(await fixture.CreateOptionsAsync(ct));
        cleanup.DropFinished("next-test");

        var remaining = await DatabaseNamesAsync(ct);
        Assert.DoesNotContain(finished, remaining);
        Assert.Contains(current, remaining);
        Assert.Contains("postgres", remaining);
        Assert.Equal("next-test", cleanup.LastPreparedTest);
    }

    [Fact]
    public void RunsBeforeEveryTestInThePostgreSqlCollection()
    {
        var test = Assert.IsAssignableFrom<IXunitTest>(TestContext.Current.Test);

        Assert.True(DropFinishedPostgreSqlDatabasesAttribute.AppliesTo(test));
        Assert.Equal(test.UniqueID, fixture.Databases.LastPreparedTest);
    }

    [Fact]
    public async Task HooksRecordAfterATestAndDropBeforeTheNext()
    {
        var ct = TestContext.Current.CancellationToken;
        var insider = Assert.IsAssignableFrom<IXunitTest>(TestContext.Current.Test);
        var database = await DatabaseNameAsync(await fixture.CreateOptionsAsync(ct));
        var hook = new DropFinishedPostgreSqlDatabasesAttribute();

        hook.After(HookMethod, insider);
        hook.Before(HookMethod, insider);

        Assert.DoesNotContain(database, await DatabaseNamesAsync(ct));
    }

    [Fact]
    public async Task HooksDoNothingForATestOutsideTheCollection()
    {
        var ct = TestContext.Current.CancellationToken;
        var insider = Assert.IsAssignableFrom<IXunitTest>(TestContext.Current.Test);
        var outsider = Substitute.For<IXunitTest>();
        outsider.UniqueID.Returns("outside-the-collection");
        outsider.TestCase.TestCollection.CollectionDefinition.Returns(typeof(FinishedTestDatabasesScopeTests));
        var database = await DatabaseNameAsync(await fixture.CreateOptionsAsync(ct));
        var hook = new DropFinishedPostgreSqlDatabasesAttribute();

        hook.After(HookMethod, outsider);
        hook.Before(HookMethod, insider);
        Assert.Contains(database, await DatabaseNamesAsync(ct));

        hook.After(HookMethod, insider);
        hook.Before(HookMethod, outsider);
        Assert.Contains(database, await DatabaseNamesAsync(ct));
        Assert.Equal(insider.UniqueID, fixture.Databases.LastPreparedTest);
    }

    private static readonly System.Reflection.MethodInfo HookMethod =
        typeof(FinishedTestDatabasesTests).GetMethod(nameof(HooksRecordAfterATestAndDropBeforeTheNext))!;

    private static async Task<string> DatabaseNameAsync(DbContextOptions<ApexRacers.Data.AppDbContext> options)
    {
        await using var db = new ApexRacers.Data.AppDbContext(options);
        return db.Database.GetDbConnection().Database;
    }

    private async Task<List<string>> DatabaseNamesAsync(CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(fixture.Container.GetConnectionString());
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT datname FROM pg_database", connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var names = new List<string>();
        while (await reader.ReadAsync(ct)) names.Add(reader.GetString(0));
        return names;
    }
}

/// <summary>The cleanup must never run for a test outside the collection: one running beside a
/// PostgreSQL test would record that test's database as finished, and it would be dropped mid-test.</summary>
public sealed class FinishedTestDatabasesScopeTests
{
    [Fact]
    public void DoesNotApplyOutsideThePostgreSqlCollection()
    {
        var test = Assert.IsAssignableFrom<IXunitTest>(TestContext.Current.Test);

        Assert.False(DropFinishedPostgreSqlDatabasesAttribute.AppliesTo(test));
    }
}
