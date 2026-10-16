namespace ApexRacers.Core.PercentileHistory;

public sealed record PercentileHistoryWeekMetadata(Guid WeekId, int SeasonId, int SeriesId, string SeriesName,
    int RaceWeekIndex, int TrackId, string TrackName, string? TrackConfigName, DateOnly StartDate);
public sealed record PercentileHistorySeasonMetadata(int SeasonId, int SeriesId, int Year, int Quarter, bool Active);
