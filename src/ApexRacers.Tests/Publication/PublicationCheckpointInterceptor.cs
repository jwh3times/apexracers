using System.Collections.Concurrent;
using System.Data.Common;
using ApexRacers.Tests.Lifecycle;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace ApexRacers.Tests.Publication;

internal sealed class PublicationCheckpointInterceptor(LifecycleGates gates,
    ConcurrentDictionary<string, Guid> admissions, ConcurrentDictionary<string, int> backends) : DbCommandInterceptor
{
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("UPDATE", StringComparison.Ordinal)
            && command.CommandText.Contains("PublicationReleases", StringComparison.Ordinal)
            && command.CommandText.Contains("TerminalAt", StringComparison.Ordinal))
        {
            var release = command.Parameters.Cast<DbParameter>().Select(p => p.Value).OfType<Guid>()
                .FirstOrDefault(admissions.Values.Contains);
            var writer = admissions.FirstOrDefault(p => p.Value == release).Key;
            if (writer is not null && command.Connection is NpgsqlConnection connection)
            {
                backends[writer] = connection.ProcessID;
                await gates.ReachAsync(writer, "checkpoint-command", cancellationToken);
            }
        }
        return result;
    }
}
