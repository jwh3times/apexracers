using System.Reflection;
using Npgsql;
using Xunit.v3;

namespace ApexRacers.Tests.Helpers;

/// <summary>
/// Keeps the shared PostgreSQL container small by dropping the databases finished tests left
/// behind (issue #568). Every test in the collection gets its own database, and a full run used to
/// leave about 660 of them (about 9 GB) for the container's removal to delete in one Docker API
/// call. That call usually took 30–55 s on CI and failed the whole collection when it outlasted
/// Docker.DotNet's 100 s request timeout.
/// </summary>
/// <remarks>
/// After each test the names of every database that exists are recorded; before the next test
/// they are dropped. The collection runs one test at a time, so a recorded database can only
/// belong to a test that has finished, whatever order the runner gives the test class's
/// construction and disposal relative to these hooks. Databases a test creates outside
/// <see cref="PostgreSqlFixture"/> (topology and restore helpers) are covered the same way. The
/// server's own maintenance database is never touched.
/// </remarks>
internal sealed class FinishedTestDatabases(string adminConnectionString)
{
    private string[] finished = [];

    /// <summary>The fixture's instance, while the collection's container is running.</summary>
    internal static FinishedTestDatabases? Current { get; set; }

    /// <summary>The last test this cleanup ran before; evidence that the hook is wired.</summary>
    public string? LastPreparedTest { get; private set; }

    /// <summary>Records every database that exists now, when a test in the collection has finished.</summary>
    public void RecordFinished()
    {
        using var connection = new NpgsqlConnection(adminConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand(
            "SELECT datname FROM pg_database WHERE NOT datistemplate AND datname <> current_database()",
            connection);
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read()) names.Add(reader.GetString(0));
        finished = [.. names];
    }

    /// <summary>Drops the databases recorded when the previous test finished.</summary>
    public void DropFinished(string preparedTest)
    {
        var names = Interlocked.Exchange(ref finished, []);
        if (names.Length > 0)
        {
            using var connection = new NpgsqlConnection(adminConnectionString);
            connection.Open();
            foreach (var name in names)
            {
                // FORCE ends any session a finished test left open, such as an idle pooled one.
                using var command = new NpgsqlCommand(
                    $"DROP DATABASE IF EXISTS \"{name.Replace("\"", "\"\"", StringComparison.Ordinal)}\" WITH (FORCE)",
                    connection);
                command.ExecuteNonQuery();
            }
        }
        LastPreparedTest = preparedTest;
    }
}

/// <summary>
/// Runs <see cref="FinishedTestDatabases"/> around each test in the PostgreSQL collection. Applied
/// to the collection definition, and it checks the collection itself as well: running for a test
/// outside it, beside a PostgreSQL test, would record that test's live database as finished.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class DropFinishedPostgreSqlDatabasesAttribute : BeforeAfterTestAttribute
{
    public static bool AppliesTo(IXunitTest test) =>
        test.TestCase.TestCollection.CollectionDefinition == typeof(PostgreSqlCollection);

    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (AppliesTo(test)) FinishedTestDatabases.Current?.DropFinished(test.UniqueID);
    }

    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (AppliesTo(test)) FinishedTestDatabases.Current?.RecordFinished();
    }
}
