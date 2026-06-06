namespace ApexRacers.Api.Dtos;

public record SeriesDto(
    int Id,
    string Name,
    int SeasonId,
    int? CurrentWeekNumber,
    string? Category,
    string? TrackName,
    string? TrackConfigName,
    int CarCount,
    int DriverCount);

public record WeekCarDto(
    int CarId,
    string CarName,
    string? ClassName,
    int EntryCount,
    double? FastestLapSeconds,
    double? MedianLapSeconds);

public record WeekDetailDto(
    string SeriesName,
    string? Category,
    string? TrackName,
    string? TrackConfigName,
    double? TrackLengthMiles,
    IReadOnlyList<WeekCarDto> Cars);

public record DistributionBin(double MinSeconds, double MaxSeconds, int Count, bool ContainsUser);

public record PercentileResultDto(
    int SeriesId,
    int WeekNumber,
    int CarId,
    long CustomerId,
    double PercentileRank,
    int SampleSize,
    DateTimeOffset ComputedAt,
    string SeriesName,
    string? TrackName,
    string? TrackConfigName,
    double YourBestLapSeconds,
    double FieldBestLapSeconds,
    double FieldMedianLapSeconds,
    IReadOnlyList<DistributionBin> Distribution);

public record CarRecommendationDto(
    int Rank,
    int CarId,
    string CarName,
    double PercentileRank,
    int SampleSize,
    double ProjectedLapSeconds,
    double? BestLapSeconds);

public record AuthResultDto(string Token, Guid UserId, string DisplayName, string? RefreshToken = null);

public record AdminUserDto(Guid UserId, string Email, string DisplayName, string Role);

public record FeatureFlagDto(
    int Id,
    string Key,
    string Name,
    string? Description,
    bool IsEnabled,
    string MinimumRole,
    DateTime CreatedAt,
    DateTime UpdatedAt);

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
    int TotalWeeks,
    IReadOnlyList<WeeklyPercentileDto> PercentileHistory);
