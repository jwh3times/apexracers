using ApexRacers.Api.Dtos;
using Xunit;

namespace ApexRacers.Tests.Dtos;

public class ResponseDtoRankNamingTests
{
    [Theory]
    [InlineData(typeof(CarRecommendationDto), "RecommendationRank")]
    [InlineData(typeof(CarStrategyDto), "RecommendationRank")]
    [InlineData(typeof(SeasonStandingDto), "Standing")]
    [InlineData(typeof(SeasonTtStandingDto), "Standing")]
    [InlineData(typeof(SeasonQualifyResultDto), "Standing")]
    [InlineData(typeof(GlobalLeaderboardEntryDto), "Standing")]
    public void RankedResponses_UseDomainSpecificPropertyName(Type dtoType, string expectedProperty)
    {
        var propertyNames = dtoType.GetProperties().Select(property => property.Name).ToList();

        Assert.Contains(expectedProperty, propertyNames);
        Assert.DoesNotContain("Rank", propertyNames);
        Assert.DoesNotContain("OptimalRank", propertyNames);
    }
}
