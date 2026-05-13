namespace ApexRacers.Api.Dtos;

public record SeriesDto(int Id, string Name, int SeasonId, int? CurrentWeekId);

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
