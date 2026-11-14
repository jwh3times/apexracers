namespace ApexRacers.Api.Services;

/// <summary>
/// Which automatic span sources the API exports to Application Insights, decided by the ordinary
/// <c>Logging:LogLevel</c> configuration (issue #527).
///
/// <para>App logs always export, through the same filters that govern every other log sink. Spans
/// are a wider copy: a request span carries the raw path, an outgoing-HTTP span the upstream URL,
/// and a database span the SQL text. So each source stays off unless its framework's log category
/// resolves to <see cref="LogLevel.Debug"/> or lower, which an operator sets deliberately while
/// investigating, for example with <c>Logging__LogLevel__Npgsql=Debug</c>.</para>
///
/// <para>Raising a category here exports that source's spans only. The code-level privacy filters
/// in <c>EvidenceOperationalLogging</c> still suppress the framework's own log lines.</para>
/// </summary>
public sealed record TelemetrySpans(bool Requests, bool OutgoingHttp, bool Database)
{
    public const string RequestsCategory = "Microsoft.AspNetCore";
    public const string OutgoingHttpCategory = "System.Net.Http";
    public const string DatabaseCategory = "Npgsql";

    public bool Any => Requests || OutgoingHttp || Database;

    public static TelemetrySpans FromLogLevels(IConfiguration configuration)
    {
        var levels = configuration.GetSection("Logging:LogLevel");
        return new(
            Requests: IsDebugOrLower(levels, RequestsCategory),
            OutgoingHttp: IsDebugOrLower(levels, OutgoingHttpCategory),
            Database: IsDebugOrLower(levels, DatabaseCategory));
    }

    // The logger's own rule: the longest configured prefix of the category wins, then Default.
    private static bool IsDebugOrLower(IConfigurationSection levels, string category)
    {
        for (var prefix = category; ; prefix = prefix[..prefix.LastIndexOf('.')])
        {
            if (TryLevel(levels[prefix], out var level))
                return level <= LogLevel.Debug;
            if (!prefix.Contains('.'))
                break;
        }

        return TryLevel(levels["Default"], out var fallback) && fallback <= LogLevel.Debug;
    }

    private static bool TryLevel(string? value, out LogLevel level) =>
        Enum.TryParse(value?.Trim(), ignoreCase: true, out level);
}
