using ApexRacers.Core.Models;

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

/// <summary>
/// The fastest Uploaded Lap a Driver holds for this Car and Track that the Race Week's bound
/// excluded, with the date it was driven. Present only when it is faster than the Personal Best
/// that was ranked — a slower excluded lap would have changed nothing, so reporting it would be
/// noise rather than disclosure.
/// </summary>
public record UploadedBestOutsideWeekDto(double LapSeconds, DateTimeOffset RecordedAt);

/// <summary>
/// A Subject Driver's standing in one Race Week's Field for one Car. <c>YourBestLapEvidence</c>
/// names which evidence produced <c>YourBestLapSeconds</c>; the Field itself is composed entirely
/// of Race Laps, so an Uploaded Lap is ranked against laps of a kind it is not.
/// </summary>
public record PercentileResultDto(
    int SeriesId,
    int WeekNumber,
    int CarId,
    long CustomerId,
    double PercentileRank,
    int FieldPosition,
    int TopSharePercent,
    int SampleSize,
    bool IsPercentilePresentable,
    DateTimeOffset ComputedAt,
    string SeriesName,
    string? TrackName,
    string? TrackConfigName,
    double YourBestLapSeconds,
    LapEvidence YourBestLapEvidence,
    double FieldBestLapSeconds,
    double FieldMedianLapSeconds,
    IReadOnlyList<DistributionBin> Distribution,
    double? WorldRecordLapSeconds = null,
    double? WorldRecordGapSeconds = null,
    UploadedBestOutsideWeekDto? UploadedBestOutsideWeek = null);

/// <summary>
/// One ranked Car recommendation for a Race Week. <c>BestLapEvidence</c> names which evidence
/// produced <c>BestLapSeconds</c> and is null exactly when that lap is — a projected Car the
/// Driver holds no lap for. <c>PercentileRank</c>, placement, and <c>FieldSize</c> exist only when
/// the Driver has a Personal Best in this week's Field; <c>ExpectedPercentile</c> is the historical
/// average used to produce <c>ProjectedLapSeconds</c>.
/// </summary>
public record CarRecommendationDto(
    int RecommendationRank,
    int CarId,
    string CarName,
    double? PercentileRank,
    double? ExpectedPercentile,
    int? TopSharePercent,
    int? FieldSize,
    bool IsPercentilePresentable,
    double ProjectedLapSeconds,
    double? BestLapSeconds,
    LapEvidence? BestLapEvidence);

public record AuthResultDto(string Token, Guid UserId, string DisplayName, string? RefreshToken = null);

/// <summary>
/// Acknowledgement for a forgot-password request. <see cref="Message"/> is always a
/// generic confirmation (it never reveals whether the account exists); <see cref="ResetToken"/>
/// is populated only in the Development environment so the reset flow can be exercised
/// without an email provider, and is null everywhere else.
/// </summary>
public record ForgotPasswordResponse(string Message, string? ResetToken);

public record MessageResponse(string Message);

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
    string? ConfigName,
    string CarName,
    long CustomerId,
    string DriverName);

/// <summary>
/// One driver's best uploaded lap for a car at a track. Identified by <c>CarId</c> and
/// <c>TrackId</c> — <c>TrackName</c> is the venue's and is shared by every layout there, so it
/// is a label, not an identity.
/// </summary>
public record UploadedBestDto(
    int CarId,
    string CarName,
    int TrackId,
    string TrackName,
    string? ConfigName,
    double BestLapSeconds,
    int LapCount,
    DateTimeOffset LastRecordedAt);

public record WeeklyPercentileDto(
    int WeekNumber,
    string TrackName,
    string? ConfigName,
    double PercentileRank,
    int TopSharePercent,
    int SampleSize,
    DateTimeOffset ComputedAt);

/// <summary>
/// Per-Car percentile history for one Series. <c>PersonalBestLapEvidence</c> names which evidence
/// produced <c>PersonalBestLapSeconds</c> — the fastest across every Race Week counted here — and
/// is null exactly when that lap is.
/// </summary>
public record CarAnalyticsDto(
    int CarId,
    string CarName,
    int SeriesId,
    string SeriesName,
    double LatestPercentileRank,
    int LatestTopSharePercent,
    double BestPercentileRank,
    int BestTopSharePercent,
    double? PersonalBestLapSeconds,
    LapEvidence? PersonalBestLapEvidence,
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
public record DriverProgressionDto(long CustomerId, IReadOnlyList<CategoryProgressionDto> Categories);

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
    string DriverName,
    string? Country,
    string? CountryCode,
    string? MemberSince,
    IReadOnlyList<LicenseBadgeDto> Licenses,
    IReadOnlyList<CategoryCareerDto> Career,
    ThisYearSummaryDto ThisYear,
    FavoriteCarDto? FavoriteCar,
    FavoriteTrackDto? FavoriteTrack);

/// <summary>
/// One row in the driver's recent-race history. SrDelta is in SR points (sub-level / 100).
/// <c>TrackId</c> identifies the track raced (0 when iRacing named none); <c>TrackName</c> is
/// shared with every layout at that venue, so <c>ConfigName</c> is what tells them apart. The
/// configuration is null when the track carries none or is absent from the local catalog.
/// </summary>
public record RaceHistoryRowDto(
    int SubsessionId,
    DateTimeOffset StartTime,
    string SeriesName,
    int TrackId,
    string TrackName,
    string? ConfigName,
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
    long CustomerId,
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

/// <summary>One lap in a driver's per-lap trace. LapTimeSeconds is -1 when the lap is not timed.</summary>
public record LapDto(int LapNumber, double LapTimeSeconds, bool Incident, bool Timed);

/// <summary>
/// A driver's per-lap pace for one subsession plus server-computed summary stats over
/// the clean ("green", no-incident) laps. DegSlopeSecondsPerLap is the linear fit of lap
/// time vs lap number — positive means the driver slowed over the run.
/// </summary>
public record DriverLapsDto(
    int SubsessionId,
    long CustomerId,
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

/// <summary>One Race Week, including whether the caller has an Uploaded Lap at its Track.</summary>
public record ScheduleWeekDto(
    int WeekNumber,
    string TrackName,
    string? ConfigName,
    DateOnly StartDate,
    WeatherSummaryDto? Weather,
    IReadOnlyList<CarBopDto> Bop,
    bool HasUploadedLapAtTrack);

/// <summary>A series' active-season schedule (weeks ordered by week number).</summary>
public record SeasonScheduleDto(int SeriesId, string SeriesName, IReadOnlyList<ScheduleWeekDto> Weeks);

/// <summary>A car class available for a season's standings (for the class selector).</summary>
public record CarClassOptionDto(int CarClassId, string CarClassName);

/// <summary>One driver row in a season's championship standings.</summary>
public record SeasonStandingDto(
    int Standing,
    long CustomerId,
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
    int Standing,
    long CustomerId,
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
    int Standing,
    long CustomerId,
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
    int Standing,
    long CustomerId,
    string DriverName,
    string Location,
    int Starts,
    int Wins,
    int IRating,
    int TtRating,
    int ChampPoints);

// ── Rival comparison (3.1) ────────────────────────────────────────────────────

/// <summary>A driver the caller follows for head-to-head comparison.</summary>
public record RivalDto(long CustomerId, string DriverName, DateTimeOffset CreatedAt);

/// <summary>A driver-name search hit (for adding a rival).</summary>
public record DriverSearchResultDto(long CustomerId, string DriverName);

/// <summary>A suggested rival, drawn from drivers the caller has actually raced.</summary>
public record RivalSuggestionDto(long CustomerId, string DriverName, int SharedRaces);

/// <summary>iRating history for one license category (for the comparison overlay chart).</summary>
public record CategoryHistoryDto(
    int CategoryId,
    string CategoryName,
    IReadOnlyList<TimeSeriesPointDto> Points);

/// <summary>One driver's side of a head-to-head comparison.</summary>
public record ComparisonSideDto(
    long CustomerId,
    string DriverName,
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

/// <summary>
/// Best lap each driver set at a track they both raced. -1 = no valid lap. Identified by
/// <c>TrackId</c> — the name alone names the venue and is shared by every layout there.
/// </summary>
public record SharedTrackPaceDto(
    int TrackId,
    string TrackName,
    string? ConfigName,
    double YourBestLapSeconds,
    double RivalBestLapSeconds);

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

/// <summary>
/// The individually classified Drivers of one ingested subsession, plus session context. This is
/// not always the whole field iRacing classified: a Race Result names exactly one Driver, so team
/// entries and AI entries produce none and are absent from <c>Results</c> while still having held
/// finishing positions. <c>TeamEntryCount</c> and <c>AiEntryCount</c> say how many are missing —
/// both zero means the field is complete, and both null means it was never counted.
/// <c>SplitIndex</c> is zero-based with 0 the strongest Split; it and <c>SplitCount</c> are null
/// together when the Split's position is unknown, so 0 never stands in for "we don't know".
/// </summary>
public record SubsessionDetailDto(
    int SubsessionId,
    DateTimeOffset StartTime,
    string SeriesName,
    string TrackName,
    string? TrackConfigName,
    int StrengthOfField,
    int? SplitIndex,
    int? SplitCount,
    int? TeamEntryCount,
    int? AiEntryCount,
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
    IReadOnlyList<UploadedBestDto> YourUploadedBests);

/// <summary>One track configuration in the catalog browse grid.</summary>
public record TrackCatalogItemDto(
    int TrackId,
    string Name,
    string? ConfigName,
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
    string? ConfigName,
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
    IReadOnlyList<UploadedBestDto> YourUploadedBests);

// ── Strategy & setup intelligence (3.3) ──────────────────────────────────────

/// <summary>Weather-risk assessment for a strategy week (Low/Medium/High + advice).</summary>
public record WeatherRiskDto(
    string Level,
    double PrecipChancePct,
    bool FieldRainCapable,
    string Note);

/// <summary>
/// Per-car strategy context for one week: Balance of Performance, its shift vs the previous
/// week, fuel/tire notes derived from the BoP, rain capability, and — when the caller is
/// iRacing-linked — their personalized competitiveness (percentile, projected lap, recommendation rank).
/// </summary>
public record CarStrategyDto(
    int CarId,
    string CarName,
    double WeightPenaltyKg,
    double PowerAdjustPct,
    double MaxPctFuelFill,
    int MaxDryTireSets,
    double WeightDeltaKg,
    double PowerDeltaPct,
    string BopTrend,
    bool FuelCapped,
    string FuelNote,
    bool LimitedTireSets,
    string TireNote,
    bool RainEnabled,
    double? PercentileRank,
    double? ExpectedPercentile,
    int? TopSharePercent,
    int? FieldSize,
    bool IsPercentilePresentable,
    double? ProjectedLapSeconds,
    int? RecommendationRank);

/// <summary>
/// A week's strategy briefing: track + pit context, weather risk, and per-car BoP/fuel/tire
/// strategy. <see cref="Personalized"/> is true when an iRacing-linked caller's overlay is included.
/// </summary>
public record WeekStrategyDto(
    int SeriesId,
    string SeriesName,
    int WeekNumber,
    string TrackName,
    string? ConfigName,
    double? TrackLengthMiles,
    int? CornersPerLap,
    int? NumberPitstalls,
    int? PitRoadSpeedLimit,
    bool NightLighting,
    WeatherSummaryDto? Weather,
    WeatherRiskDto WeatherRisk,
    bool Personalized,
    IReadOnlyList<CarStrategyDto> Cars);

// ── Achievements / trophy case (3.4) ─────────────────────────────────────────

/// <summary>One iRacing award/achievement the driver has earned (a trophy-case tile).</summary>
public record AwardDto(
    int AwardId,
    string Name,
    string? Description,
    string? GroupName,
    int Count,
    DateTimeOffset AwardDate,
    string? IconUrl,
    string? IconBackgroundColor,
    int Progress,
    int Threshold);

/// <summary>The authenticated driver's trophy case (awards, newest first).</summary>
public record AchievementsDto(
    long CustomerId,
    int AwardCount,
    IReadOnlyList<AwardDto> Awards);

/// <summary>
/// The caller's own percentile for one car in a week (for the Week Detail "Your pct" column).
/// Only cars the caller actually has a lap for this week are returned.
/// </summary>
public record WeekCarPercentileDto(int CarId, double PercentileRank, int TopSharePercent);
