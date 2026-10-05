using Microsoft.Extensions.Logging;

namespace ApexRacers.Data;

/// <summary>Payload-capable framework/provider categories cannot bypass the application's
/// sanitized operational messages. External automatic telemetry and retention need deployment observation.</summary>
public static class EvidenceOperationalLogging
{
    public static void Configure(ILoggingBuilder logging)
    {
        logging.AddFilter("Microsoft.AspNetCore", LogLevel.None);
        logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None);
        logging.AddFilter("System.Net.Http", LogLevel.None);
        logging.AddFilter("Aydsko.iRacingData", LogLevel.None);
        logging.AddFilter("Npgsql", LogLevel.None);
    }
}
