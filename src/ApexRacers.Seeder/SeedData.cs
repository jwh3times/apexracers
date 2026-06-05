using System.Text.Json.Serialization;

namespace ApexRacers.Seeder;

// ── JSON DTOs for iRacing API response objects ────────────────────────────────

public record TrackApiEntry(
    [property: JsonPropertyName("track_id")]          int TrackId,
    [property: JsonPropertyName("track_name")]         string TrackName,
    [property: JsonPropertyName("config_name")]        string? ConfigName,
    [property: JsonPropertyName("category_id")]        int? CategoryId,
    [property: JsonPropertyName("category")]           string? Category,
    [property: JsonPropertyName("track_config_length")] double? TrackConfigLength,
    [property: JsonPropertyName("is_dirt")]            bool IsDirt,
    [property: JsonPropertyName("is_oval")]            bool IsOval,
    [property: JsonPropertyName("location")]           string? Location,
    [property: JsonPropertyName("time_zone")]          string? TimeZone,
    [property: JsonPropertyName("retired")]            bool Retired
);

public record CarApiEntry(
    [property: JsonPropertyName("car_id")]               int CarId,
    [property: JsonPropertyName("car_name")]             string CarName,
    [property: JsonPropertyName("car_name_abbreviated")] string CarNameAbbreviated,
    [property: JsonPropertyName("retired")]              bool Retired,
    [property: JsonPropertyName("free_with_subscription")] bool FreeWithSubscription,
    [property: JsonPropertyName("package_id")]           int? PackageId,
    [property: JsonPropertyName("hp")]                   int? Hp,
    [property: JsonPropertyName("car_weight")]           int? CarWeight
);

public record SeasonScheduleResponse(
    [property: JsonPropertyName("schedules")] List<ScheduleWeekEntry> Schedules
);

public record ScheduleWeekEntry(
    [property: JsonPropertyName("season_id")]        int SeasonId,
    [property: JsonPropertyName("season_name")]      string SeasonName,
    [property: JsonPropertyName("series_id")]        int SeriesId,
    [property: JsonPropertyName("series_name")]      string SeriesName,
    [property: JsonPropertyName("race_week_num")]    int RaceWeekNum,
    [property: JsonPropertyName("start_date")]       string StartDate,
    [property: JsonPropertyName("track")]            ScheduleTrackRef Track,
    [property: JsonPropertyName("car_restrictions")] List<CarRestrictionEntry> CarRestrictions
);

public record ScheduleTrackRef(
    [property: JsonPropertyName("track_id")]   int TrackId,
    [property: JsonPropertyName("track_name")] string TrackName,
    [property: JsonPropertyName("config_name")] string? ConfigName
);

public record CarRestrictionEntry(
    [property: JsonPropertyName("car_id")] int CarId
);

// ── Car class DTOs ────────────────────────────────────────────────────────────

public record CarClassApiEntry(
    [property: JsonPropertyName("car_class_id")]  int CarClassId,
    [property: JsonPropertyName("name")]          string Name,
    [property: JsonPropertyName("short_name")]    string ShortName,
    [property: JsonPropertyName("relative_speed")] int RelativeSpeed,
    [property: JsonPropertyName("cars_in_class")] List<CarClassMember> CarsInClass
);

public record CarClassMember(
    [property: JsonPropertyName("car_id")] int CarId
);

public record SeriesApiEntry(
    [property: JsonPropertyName("series_id")]      int SeriesId,
    [property: JsonPropertyName("category_id")]    int? CategoryId,
    [property: JsonPropertyName("category")]       string? Category,
    [property: JsonPropertyName("allowed_licenses")] List<AllowedLicense> AllowedLicenses
);

public record AllowedLicense(
    [property: JsonPropertyName("license_group")] int LicenseGroup
);

public record SeasonApiEntry(
    [property: JsonPropertyName("season_id")]    int SeasonId,
    [property: JsonPropertyName("series_id")]    int SeriesId,
    [property: JsonPropertyName("car_class_ids")] List<int> CarClassIds,
    [property: JsonPropertyName("license_group")] int LicenseGroup,
    [property: JsonPropertyName("official")]     bool Official,
    [property: JsonPropertyName("drops")]        int Drops,
    [property: JsonPropertyName("fixed_setup")]  bool FixedSetup,
    [property: JsonPropertyName("multiclass")]   bool Multiclass
);
