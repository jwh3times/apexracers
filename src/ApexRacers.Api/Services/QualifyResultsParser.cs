using System.Text.Json;
using ApexRacers.Api.Dtos;

namespace ApexRacers.Api.Services;

/// <summary>
/// Parses iRacing's season-qualify-results chunk JSON (an array of driver rows) into
/// ranked qualifying results. The Aydsko 2603.0.0 <c>SeasonQualifyResult</c> type does
/// NOT model the real payload (it has no <c>best_qual_lap_time</c>), so the chunks are
/// downloaded from the response header's <c>chunk_info</c> and parsed here directly. Pure +
/// static so it can be unit-tested against the captured sample.
/// </summary>
public static class QualifyResultsParser
{
    /// <summary>Best-lap value (ten-thousandths) at or below this means "no valid lap".</summary>
    public const double NoLapSentinel = -1;

    public static IReadOnlyList<SeasonQualifyResultDto> Parse(IEnumerable<string> chunkJsonDocs)
    {
        var results = new List<SeasonQualifyResultDto>();

        foreach (var doc in chunkJsonDocs)
        {
            if (string.IsNullOrWhiteSpace(doc))
                continue;

            using var json = JsonDocument.Parse(doc);
            if (json.RootElement.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var row in json.RootElement.EnumerateArray())
            {
                var lapTicks = GetInt(row, "best_qual_lap_time");
                results.Add(new SeasonQualifyResultDto(
                    Rank: GetInt(row, "rank"),
                    CustId: GetLong(row, "cust_id"),
                    DriverName: GetString(row, "display_name"),
                    Division: GetInt(row, "division"),
                    IRating: GetNullableInt(GetLicense(row), "irating"),
                    BestQualLapSeconds: lapTicks > 0 ? lapTicks / 10000.0 : NoLapSentinel,
                    Week: GetInt(row, "week")));
            }
        }

        return results;
    }

    private static JsonElement? GetLicense(JsonElement row) =>
        row.TryGetProperty("license", out var lic) && lic.ValueKind == JsonValueKind.Object
            ? lic
            : null;

    private static int GetInt(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : 0;

    private static long GetLong(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.TryGetInt64(out var i) ? i : 0;

    private static string GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? string.Empty
            : string.Empty;

    private static int? GetNullableInt(JsonElement? el, string name) =>
        el is { } e && e.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : null;
}
