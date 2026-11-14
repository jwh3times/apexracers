namespace ApexRacers.Api.Services;

/// <summary>
/// Which automatic span sources the API exports to Application Insights, decided by the ordinary
/// <c>Logging:LogLevel</c> configuration (issue #527).
///
/// <para>App logs always export, through the same filters that govern every other log sink. Spans
/// are a wider copy: a request span carries the raw path, an outgoing-HTTP span the upstream URL,
/// and a database span the SQL text. So each source stays off unless its framework's own log
/// category (or a parent of it, never <c>Default</c>) is set to <see cref="LogLevel.Debug"/> or
/// lower, which an operator does deliberately while investigating, for example with
/// <c>Logging__LogLevel__Npgsql=Debug</c>.</para>
///
/// <para>Raising a category here exports that source's spans only. The code-level privacy filters
/// in <c>EvidenceOperationalLogging</c> still suppress the framework's own log lines.</para>
/// </summary>
public sealed record SpanExportSources(bool Requests, bool OutgoingHttp, bool Database)
{
    public const string RequestsCategory = "Microsoft.AspNetCore";
    public const string OutgoingHttpCategory = "System.Net.Http";
    public const string DatabaseCategory = "Npgsql";

    public bool Any => Requests || OutgoingHttp || Database;

    public static SpanExportSources FromLogLevels(IConfiguration configuration)
    {
        var levels = configuration.GetSection("Logging:LogLevel");
        return new(
            Requests: IsDebugOrLower(levels, RequestsCategory),
            OutgoingHttp: IsDebugOrLower(levels, OutgoingHttpCategory),
            Database: IsDebugOrLower(levels, DatabaseCategory));
    }

    // The logger's prefix rule without its Default fallback: the longest configured prefix of the
    // category wins, and an unconfigured category exports nothing. Default is left out on purpose:
    // raising it is the routine way to chase an app bug, and must not quietly widen the export.
    // Provider-scoped sections (Logging:OpenTelemetry:LogLevel) and wildcard categories are not
    // consulted either: they tune log sinks, and this decides span export.
    private static bool IsDebugOrLower(IConfigurationSection levels, string category)
    {
        for (var prefix = category; ; prefix = prefix[..prefix.LastIndexOf('.')])
        {
            if (TryLevel(levels[prefix], out var level))
                return level <= LogLevel.Debug;
            if (!prefix.Contains('.'))
                return false;
        }
    }

    private static bool TryLevel(string? value, out LogLevel level) =>
        Enum.TryParse(value?.Trim(), ignoreCase: true, out level);
}
