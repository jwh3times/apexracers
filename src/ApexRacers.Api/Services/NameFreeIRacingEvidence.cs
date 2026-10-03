using ApexRacers.Api.Dtos;

namespace ApexRacers.Api.Services;

/// <summary>Owned mapped contracts only; does not rewrite arbitrary JSON or SDK wire objects.</summary>
internal static class NameFreeIRacingEvidence
{
    public static T Map<T>(T value) => value switch
    {
        ProfileSnapshot profile => (T)(object)(profile with { DisplayName = string.Empty }),
        SeasonStandingDto[] rows => (T)(object)rows.Select(r => r with { DriverName = string.Empty }).ToArray(),
        GlobalLeaderboardEntryDto[] rows => (T)(object)rows.Select(r => r with { DriverName = string.Empty }).ToArray(),
        SeasonTtStandingDto[] rows => (T)(object)rows.Select(r => r with { DriverName = string.Empty }).ToArray(),
        SeasonQualifyResultDto[] rows => (T)(object)rows.Select(r => r with { DriverName = string.Empty }).ToArray(),
        DriverSearchResultDto[] rows => (T)(object)rows.Select(r => r with { DriverName = string.Empty }).ToArray(),
        IReadOnlyList<SeasonStandingDto> rows => (T)(object)rows.Select(r => r with { DriverName = string.Empty }).ToList(),
        IReadOnlyList<GlobalLeaderboardEntryDto> rows => (T)(object)rows.Select(r => r with { DriverName = string.Empty }).ToList(),
        IReadOnlyList<SeasonTtStandingDto> rows => (T)(object)rows.Select(r => r with { DriverName = string.Empty }).ToList(),
        IReadOnlyList<SeasonQualifyResultDto> rows => (T)(object)rows.Select(r => r with { DriverName = string.Empty }).ToList(),
        IReadOnlyList<DriverSearchResultDto> rows => (T)(object)rows.Select(r => r with { DriverName = string.Empty }).ToList(),
        _ => value,
    };
}
