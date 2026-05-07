namespace ApexRacers.Api.Dtos;

public record SeriesDto(int Id, string Name, int CurrentSeason);

public record WeekCarDto(
    int CarId,
    string CarName,
    int EntryCount,
    double? FastestLapSeconds,
    double? MedianLapSeconds);

public record PercentileResultDto(
    int SeriesId,
    int WeekId,
    int CarId,
    long CustomerId,
    double PercentileRank,
    int SampleSize,
    DateTimeOffset ComputedAt);

public record CarRecommendationDto(
    int Rank,
    int CarId,
    string CarName,
    double PercentileRank,
    int SampleSize);

public record AuthResultDto(string Token, long CustomerId, string DisplayName);
