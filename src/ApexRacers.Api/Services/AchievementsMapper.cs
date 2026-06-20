using ApexRacers.Api.Dtos;
using Aydsko.iRacingData.Member;

namespace ApexRacers.Api.Services;

/// <summary>
/// Pure SDK → DTO mapping for the awards trophy case. Extracted as a tested static helper so the
/// field selection (description fallback, icon preference, date normalization) is unit-tested
/// directly without an HTTP round-trip (mirrors <see cref="CarCatalogMapper"/>).
/// </summary>
public static class AchievementsMapper
{
    public static AwardDto MapAward(MemberAward a) => new(
        a.AwardId,
        a.Name ?? string.Empty,
        // The per-instance "awarded" description is richer than the generic one; fall back to it.
        string.IsNullOrWhiteSpace(a.AwardedDescription) ? NullIfBlank(a.Description) : a.AwardedDescription,
        NullIfBlank(a.GroupName),
        a.AwardCount <= 0 ? 1 : a.AwardCount,
        // AwardDate is a calendar date (DateOnly); treat it as midnight UTC.
        new DateTimeOffset(a.AwardDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
        NullIfBlank(a.IconUrlLarge) ?? NullIfBlank(a.IconUrlSmall),
        NullIfBlank(a.IconBackgroundColor),
        a.Progress,
        a.Threshold);

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
