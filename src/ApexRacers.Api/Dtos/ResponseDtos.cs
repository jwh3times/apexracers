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
    IReadOnlyList<DistributionBin> Distribution,
    double? WorldRecordLapSeconds = null,
    double? WorldRecordGapSeconds = null);

public record CarRecommendationDto(
    int Rank,
    int CarId,
    string CarName,
    double PercentileRank,
    int SampleSize,
    double ProjectedLapSeconds,
    double? BestLapSeconds);

public record AuthResultDto(string Token, Guid UserId, string DisplayName, string? RefreshToken = null);

/// <summary>
/// Acknowledgement for a forgot-password request. <see cref="Message"/> is always a
/// generic confirmation (it never reveals whether the account exists); <see cref="ResetToken"/>
/// is populated only in the Development environment so the reset flow can be exercised
/// without an email provider, and is null everywhere else.
/// </summary>
public record ForgotPasswordResponse(string Message, string? ResetToken);

/// <summary>Typed body for the 409 returned when the caller has no linked iRacing customer id.</summary>
public record NotLinkedDto(string Code, string Message);

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

/// <summary>A single point in a member chart time series (iRating/SR/TT over time).</summary>
public record TimeSeriesPointDto(string When, int Value);

/// <summary>Current standing plus iRating history for one license category.</summary>
public record CategoryProgressionDto(
    int CategoryId,
    string CategoryName,
    int IRating,
    double SafetyRating,
    double Cpi,
    int LicenseLevel,
    string GroupName,
    int TtRating,
    string Color,
    IReadOnlyList<TimeSeriesPointDto> IRatingHistory);

/// <summary>A driver's per-category progression (one card per license category).</summary>
public record MemberProgressionDto(long CustomerId, IReadOnlyList<CategoryProgressionDto> Categories);

/// <summary>Current license standing for one category (for the colored profile badges).</summary>
public record LicenseBadgeDto(
    int CategoryId,
    string CategoryName,
    string GroupName,
    int LicenseLevel,
    double SafetyRating,
    int IRating,
    string Color);

/// <summary>Lifetime career stats for one license category.</summary>
public record CategoryCareerDto(
    int CategoryId,
    string CategoryName,
    int Starts,
    int Wins,
    int Top5,
    int Poles,
    int AvgStartPosition,
    int AvgFinishPosition,
    int Laps,
    int LapsLed,
    double WinPercentage,
    double Top5Percentage);

/// <summary>This-year activity summary (official + league sessions/wins).</summary>
public record ThisYearSummaryDto(
    int OfficialSessions,
    int OfficialWins,
    int LeagueSessions,
    int LeagueWins);

public record FavoriteCarDto(int CarId, string CarName, string? ImageUrl);

public record FavoriteTrackDto(int TrackId, string TrackName, string? ConfigName, string? LogoUrl);

/// <summary>Enriched driver profile: identity, license badges, career stats, recap favorites.</summary>
public record DriverProfileDto(
    long CustomerId,
    string DisplayName,
    string? Country,
    string? CountryCode,
    string? MemberSince,
    IReadOnlyList<LicenseBadgeDto> Licenses,
    IReadOnlyList<CategoryCareerDto> Career,
    ThisYearSummaryDto ThisYear,
    FavoriteCarDto? FavoriteCar,
    FavoriteTrackDto? FavoriteTrack);

/// <summary>One row in the driver's recent-race history. SrDelta is in SR points (sub-level / 100).</summary>
public record RaceHistoryRowDto(
    int SubsessionId,
    DateTimeOffset StartTime,
    string SeriesName,
    string TrackName,
    int CarId,
    string CarName,
    int StartPosition,
    int FinishPosition,
    int Incidents,
    int IRatingDelta,
    double SrDelta,
    int StrengthOfField,
    int Points);

/// <summary>Session weather summary for a race-detail header.</summary>
public record WeatherDto(double TempCelsius, int RelHumidity, double WindKph, int Skies, double PrecipChance);

/// <summary>One classified driver in a subsession result. SrDelta is in SR points (sub-level / 100).</summary>
public record SubsessionResultRowDto(
    long CustId,
    string DriverName,
    int FinishPosition,
    int StartPosition,
    double BestLapSeconds,
    double AverageLapSeconds,
    double Interval,
    int LapsLead,
    int Incidents,
    int Division,
    int IRatingDelta,
    double SrDelta);

/// <summary>One lap in a driver's per-lap trace. LapTimeSeconds is -1 when the lap has no time.</summary>
public record LapDto(int LapNumber, double LapTimeSeconds, bool Incident, bool Valid);

/// <summary>
/// A driver's per-lap pace for one subsession plus server-computed summary stats over
/// the clean ("green", no-incident) laps. DegSlopeSecondsPerLap is the linear fit of lap
/// time vs lap number — positive means the driver slowed over the run.
/// </summary>
public record DriverLapsDto(
    int SubsessionId,
    long CustId,
    double MeanSeconds,
    double StdDevSeconds,
    double FastestLapSeconds,
    double DegSlopeSecondsPerLap,
    IReadOnlyList<LapDto> Laps);

/// <summary>Forecast summary for a schedule week. Temps in °C, winds in km/h, precip as a %.</summary>
public record WeatherSummaryDto(
    double TempHighC,
    double TempLowC,
    double PrecipChancePct,
    double WindHighKph,
    double WindLowKph,
    int Skies);

/// <summary>Per-car Balance of Performance for one schedule week.</summary>
public record CarBopDto(
    int CarId,
    string CarName,
    double WeightPenaltyKg,
    double PowerAdjustPct,
    double MaxPctFuelFill,
    int MaxDryTireSets);

/// <summary>One week of a season schedule with forecast, BoP, and the caller's PB overlay.</summary>
public record ScheduleWeekDto(
    int WeekNumber,
    string TrackName,
    string ConfigName,
    DateOnly StartDate,
    WeatherSummaryDto? Weather,
    IReadOnlyList<CarBopDto> Bop,
    bool HasPersonalBest);

/// <summary>A series' active-season schedule (weeks ordered by week number).</summary>
public record SeasonScheduleDto(int SeriesId, string SeriesName, IReadOnlyList<ScheduleWeekDto> Weeks);

/// <summary>A car class available for a season's standings (for the class selector).</summary>
public record CarClassOptionDto(int CarClassId, string CarClassName);

/// <summary>One driver row in a season's championship standings.</summary>
public record SeasonStandingDto(
    int Rank,
    long CustId,
    string DriverName,
    int Division,
    int Starts,
    int Wins,
    int Top5,
    int Poles,
    int Points,
    double AvgFinishPosition,
    int Incidents);

/// <summary>Championship driver standings for a series' active season + chosen car class.</summary>
public record SeasonStandingsDto(
    int SeriesId,
    string SeriesName,
    int CarClassId,
    string CarClassName,
    IReadOnlyList<CarClassOptionDto> CarClasses,
    IReadOnlyList<SeasonStandingDto> Standings);

/// <summary>One driver row in a season's Time Trial standings.</summary>
public record SeasonTtStandingDto(
    int Rank,
    long CustId,
    string DriverName,
    int Division,
    int? TtRating,
    int Starts,
    int Wins,
    int Top5,
    int Poles,
    int Points,
    double AvgFinishPosition,
    int Incidents);

/// <summary>Time Trial standings for a series' active season + chosen car class.</summary>
public record SeasonTtStandingsDto(
    int SeriesId,
    string SeriesName,
    int CarClassId,
    string CarClassName,
    IReadOnlyList<CarClassOptionDto> CarClasses,
    IReadOnlyList<SeasonTtStandingDto> Standings);

/// <summary>One driver row in a race week's season qualifying results (best qualifying lap).</summary>
public record SeasonQualifyResultDto(
    int Rank,
    long CustId,
    string DriverName,
    int Division,
    int? IRating,
    double BestQualLapSeconds,
    int Week);

/// <summary>
/// Season qualifying results for a series' active season, chosen car class + race week.
/// <see cref="RaceWeekNum"/> is the 0-based iRacing week; <see cref="AvailableWeeks"/> lists
/// the season's known weeks for the selector.
/// </summary>
public record SeasonQualifyResultsDto(
    int SeriesId,
    string SeriesName,
    int CarClassId,
    string CarClassName,
    IReadOnlyList<CarClassOptionDto> CarClasses,
    int RaceWeekNum,
    IReadOnlyList<int> AvailableWeeks,
    IReadOnlyList<SeasonQualifyResultDto> Results);

/// <summary>An official session starting soon (race-now live guide), newest start first.</summary>
public record RaceGuideEntryDto(
    int SeriesId,
    string SeriesName,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    int EntryCount,
    int RaceWeekNum);

/// <summary>One driver row in a category's global leaderboard (ranked by iRating).</summary>
public record GlobalLeaderboardEntryDto(
    int CategoryId,
    int Rank,
    long CustId,
    string Driver,
    string Location,
    int Starts,
    int Wins,
    int IRating,
    int TtRating,
    int ChampPoints);

// ── Rival comparison (3.1) ────────────────────────────────────────────────────

/// <summary>A driver the caller follows for head-to-head comparison.</summary>
public record RivalDto(long CustId, string DisplayName, DateTimeOffset CreatedAt);

/// <summary>A driver-name search hit (for adding a rival).</summary>
public record DriverSearchResultDto(long CustId, string DisplayName);

/// <summary>A suggested rival, drawn from drivers the caller has actually raced.</summary>
public record RivalSuggestionDto(long CustId, string DisplayName, int SharedRaces);

/// <summary>iRating history for one license category (for the comparison overlay chart).</summary>
public record CategoryHistoryDto(
    int CategoryId,
    string CategoryName,
    IReadOnlyList<TimeSeriesPointDto> Points);

/// <summary>One driver's side of a head-to-head comparison.</summary>
public record ComparisonSideDto(
    long CustId,
    string DisplayName,
    string? Country,
    string? CountryCode,
    string? MemberSince,
    IReadOnlyList<LicenseBadgeDto> Licenses,
    IReadOnlyList<CategoryCareerDto> Career,
    IReadOnlyList<CategoryHistoryDto> IRatingHistory);

/// <summary>One race both drivers ran. Finish positions are overall; lower is better.</summary>
public record SharedRaceRowDto(
    int SubsessionId,
    DateTimeOffset StartTime,
    string TrackName,
    int YourFinish,
    int RivalFinish,
    int YourIRatingDelta,
    int RivalIRatingDelta,
    int YourIncidents,
    int RivalIncidents);

/// <summary>Best lap each driver set at a track they both raced. -1 = no valid lap.</summary>
public record SharedTrackPaceDto(string TrackName, double YourBestLapSeconds, double RivalBestLapSeconds);

/// <summary>
/// Head-to-head record over races both drivers ran: totals, who finished ahead more often,
/// the race rows (newest first) and best-lap-per-shared-track pace.
/// </summary>
public record SharedRaceSummaryDto(
    int TotalShared,
    int YouAhead,
    int RivalAhead,
    IReadOnlyList<SharedRaceRowDto> Races,
    IReadOnlyList<SharedTrackPaceDto> TrackPace);

/// <summary>A full driver-vs-driver comparison: both sides plus their shared-race head-to-head.</summary>
public record DriverComparisonDto(
    ComparisonSideDto You,
    ComparisonSideDto Rival,
    SharedRaceSummaryDto Shared);

/// <summary>Full classified field plus session context for one ingested subsession.</summary>
public record SubsessionDetailDto(
    int SubsessionId,
    DateTimeOffset StartTime,
    string SeriesName,
    string TrackName,
    string? TrackConfigName,
    int StrengthOfField,
    int NumCautions,
    int NumLeadChanges,
    int CornersPerLap,
    double EventBestLapSeconds,
    double EventAverageLapSeconds,
    int EventLapsComplete,
    WeatherDto? Weather,
    IReadOnlyList<SubsessionResultRowDto> Results);

// ── Catalog explorer (3.5) ──────────────────────────────────────────────────────

/// <summary>A car class a car belongs to (for the car detail page).</summary>
public record CarClassRefDto(int CarClassId, string Name);

/// <summary>One car in the catalog browse grid.</summary>
public record CarCatalogItemDto(
    int CarId,
    string Name,
    string NameAbbreviated,
    string? Make,
    string? Model,
    int? Hp,
    int? Weight,
    bool RainEnabled,
    bool FreeWithSubscription,
    IReadOnlyList<string> Categories,
    string? SmallImageUrl);

/// <summary>Full car detail: specs, images, car classes, and the caller's personal bests.</summary>
public record CarCatalogDetailDto(
    int CarId,
    string Name,
    string NameAbbreviated,
    string? Make,
    string? Model,
    int? Hp,
    int? Weight,
    bool RainEnabled,
    bool FreeWithSubscription,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> CarTypes,
    string? SmallImageUrl,
    string? LargeImageUrl,
    string? LogoUrl,
    IReadOnlyList<CarClassRefDto> CarClasses,
    IReadOnlyList<PersonalLapDto> YourBestLaps);

/// <summary>One track configuration in the catalog browse grid.</summary>
public record TrackCatalogItemDto(
    int TrackId,
    string Name,
    string ConfigName,
    string? Category,
    double? LengthMiles,
    int? CornersPerLap,
    string? Location,
    bool NightLighting,
    string? SmallImageUrl);

/// <summary>Full track detail: specs, images, map, and the caller's personal bests.</summary>
public record TrackCatalogDetailDto(
    int TrackId,
    string Name,
    string ConfigName,
    string? Category,
    double? LengthMiles,
    int? CornersPerLap,
    string? Location,
    bool NightLighting,
    double? Latitude,
    double? Longitude,
    int? PitRoadSpeedLimit,
    int? NumberPitstalls,
    bool HasSvgMap,
    string? SmallImageUrl,
    string? LargeImageUrl,
    string? TrackMapUrl,
    IReadOnlyList<PersonalLapDto> YourBestLaps);
