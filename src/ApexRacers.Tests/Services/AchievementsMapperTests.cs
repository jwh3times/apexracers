using ApexRacers.Api.Services;
using Aydsko.iRacingData.Member;
using Xunit;

namespace ApexRacers.Tests.Services;

public class AchievementsMapperTests
{
    private static MemberAward Award(
        int id = 1, string? name = "Hat Trick", string? awardedDesc = "Won 3 in a row",
        string? desc = "Generic desc", string? group = "Racing", int count = 1,
        string? large = "https://img/large.png", string? small = "https://img/small.png",
        string? bg = "#112233", int progress = 3, int threshold = 3) => new()
    {
        // The SDK types these strings as non-nullable, but real payloads can omit them; the
        // null-forgiving operator lets us pass runtime nulls to exercise the mapper's guards.
        AwardId = id,
        Name = name!,
        AwardedDescription = awardedDesc!,
        Description = desc!,
        GroupName = group!,
        AwardCount = count,
        AwardDate = new DateOnly(2026, 5, 1),
        IconUrlLarge = large!,
        IconUrlSmall = small!,
        IconBackgroundColor = bg!,
        Progress = progress,
        Threshold = threshold,
    };

    [Fact]
    public void MapAward_MapsAllFields_PrefersAwardedDescriptionAndLargeIcon()
    {
        var dto = AchievementsMapper.MapAward(Award());

        Assert.Equal(1, dto.AwardId);
        Assert.Equal("Hat Trick", dto.Name);
        Assert.Equal("Won 3 in a row", dto.Description); // awarded description wins
        Assert.Equal("Racing", dto.GroupName);
        Assert.Equal(1, dto.Count);
        Assert.Equal("https://img/large.png", dto.IconUrl); // large preferred
        Assert.Equal("#112233", dto.IconBackgroundColor);
        Assert.Equal(3, dto.Progress);
        Assert.Equal(3, dto.Threshold);
        Assert.Equal(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero), dto.AwardDate);
    }

    [Fact]
    public void MapAward_BlankAwardedDescription_FallsBackToDescription()
    {
        var dto = AchievementsMapper.MapAward(Award(awardedDesc: "  "));
        Assert.Equal("Generic desc", dto.Description);
    }

    [Fact]
    public void MapAward_BothDescriptionsBlank_IsNull()
    {
        var dto = AchievementsMapper.MapAward(Award(awardedDesc: null, desc: ""));
        Assert.Null(dto.Description);
    }

    [Fact]
    public void MapAward_BlankLargeIcon_FallsBackToSmall_ThenNull()
    {
        Assert.Equal("https://img/small.png", AchievementsMapper.MapAward(Award(large: " ")).IconUrl);
        Assert.Null(AchievementsMapper.MapAward(Award(large: null, small: null)).IconUrl);
    }

    [Theory]
    [InlineData(0, 1)]   // unset count floors to 1
    [InlineData(-3, 1)]  // negative floors to 1
    [InlineData(5, 5)]   // earned multiple times preserved
    public void MapAward_CountFlooredToAtLeastOne(int input, int expected) =>
        Assert.Equal(expected, AchievementsMapper.MapAward(Award(count: input)).Count);

    [Fact]
    public void MapAward_BlankGroupAndColor_AreNull()
    {
        var dto = AchievementsMapper.MapAward(Award(group: "", bg: "   "));
        Assert.Null(dto.GroupName);
        Assert.Null(dto.IconBackgroundColor);
    }

    [Fact]
    public void MapAward_NullName_BecomesEmptyString()
    {
        var dto = AchievementsMapper.MapAward(Award(name: null));
        Assert.Equal(string.Empty, dto.Name);
    }
}
