namespace ApexRacers.Api.Dtos;

public record SeriesDto(int Id, string Name, int SeasonId, int? CurrentWeekNumber);

public record WeekCarDto(
    int CarId,
    string CarName,
    int EntryCount,
    double? FastestLapSeconds,
    double? MedianLapSeconds);

public record PercentileResultDto(
    int SeriesId,
    int WeekNumber,
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
    int SampleSize,
    double EstimatedLapSeconds,
    bool IsProjected);

public record AuthResultDto(string Token, Guid UserId, string DisplayName);

public record TelemetryUploadResultDto(
    int TotalLaps,
    int ValidLaps,
    double? BestLapSeconds,
    string TrackName,
    string ConfigName,
    string CarName,
    long CustomerId,
    string DriverName);

public record PersonalLapDto(
    int CarId,
    string CarName,
    string TrackName,
    string ConfigName,
    double BestLapSeconds,
    int LapCount,
    DateTimeOffset LastRecordedAt);

public record WeeklyPercentileDto(
    int WeekNumber,
    string TrackName,
    string ConfigName,
    double PercentileRank,
    int SampleSize,
    DateTimeOffset ComputedAt);

public record CarAnalyticsDto(
    int CarId,
    string CarName,
    int SeriesId,
    string SeriesName,
    double LatestPercentileRank,
    double BestPercentileRank,
    double? PersonalBestLapSeconds,
    double? MedianLapSeconds,
    int TotalLaps,
    IReadOnlyList<WeeklyPercentileDto> PercentileHistory);
