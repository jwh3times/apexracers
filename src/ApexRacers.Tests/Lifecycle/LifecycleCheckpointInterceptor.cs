using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace ApexRacers.Tests.Lifecycle;

/// <summary>Hold an actual open PostgreSQL connection before its terminal UPDATE, never substitute the command.</summary>
internal sealed class LifecycleCheckpointInterceptor(
    LifecycleGates gates, ConcurrentDictionary<string, Guid> admissions,
    ConcurrentDictionary<string, int> backends) : DbCommandInterceptor
{
    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("DriverPublicationAdmissions", StringComparison.Ordinal)
            && command.CommandText.Contains("SET", StringComparison.Ordinal)
            && command.CommandText.Contains("TerminalAt", StringComparison.Ordinal))
        {
            var id = command.Parameters.Cast<DbParameter>().Select(p => p.Value).OfType<Guid>()
                .FirstOrDefault(value => admissions.Values.Contains(value));
            var writer = admissions.FirstOrDefault(pair => pair.Value == id).Key;
            if (writer is not null && command.Connection is NpgsqlConnection connection)
            {
                backends[writer] = connection.ProcessID;
                await gates.ReachAsync(writer, "checkpoint-command", cancellationToken);
            }
        }
        return result;
    }
}
