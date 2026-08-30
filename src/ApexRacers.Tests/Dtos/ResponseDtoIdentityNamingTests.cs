using System.Text.Json;
using ApexRacers.Api.Dtos;
using Xunit;

namespace ApexRacers.Tests.Dtos;

public class ResponseDtoIdentityNamingTests
{
    private const long CustomerId = 260514;
    private const string DriverName = "Jerry Holland";

    [Fact]
    public void RivalDto_SerializesDriverIdentityInDomainLanguage()
    {
        var dto = new RivalDto(260514, "Jerry Holland", DateTimeOffset.UnixEpoch);

        var json = JsonSerializer.SerializeToElement(dto, JsonSerializerOptions.Web);
        var propertyNames = json.EnumerateObject().Select(property => property.Name).ToList();

        Assert.Equal(260514, json.GetProperty("customerId").GetInt64());
        Assert.Equal("Jerry Holland", json.GetProperty("driverName").GetString());
        Assert.DoesNotContain("custId", propertyNames);
        Assert.DoesNotContain("displayName", propertyNames);
    }

    [Fact]
    public void DriverIdentityResponses_SerializeCustomerIdInDomainLanguage()
    {
        foreach (var dto in DriverIdentityResponses())
        {
            var json = Serialize(dto);
            var propertyNames = json.EnumerateObject().Select(property => property.Name).ToList();

            Assert.True(
                json.TryGetProperty("customerId", out var value),
                $"{dto.GetType().Name} must expose customerId.");
            Assert.Equal(CustomerId, value.GetInt64());
            Assert.DoesNotContain("custId", propertyNames);
        }
    }

    [Fact]
    public void DriverNameResponses_SerializeDriverNameDistinctFromUserDisplayName()
    {
        foreach (var dto in DriverNameResponses())
        {
            var json = Serialize(dto);
            var propertyNames = json.EnumerateObject().Select(property => property.Name).ToList();

            Assert.True(
                json.TryGetProperty("driverName", out var value),
                $"{dto.GetType().Name} must expose driverName.");
            Assert.Equal(DriverName, value.GetString());
            Assert.DoesNotContain("displayName", propertyNames);
            Assert.DoesNotContain("driver", propertyNames);
        }
    }

    private static JsonElement Serialize(object dto) =>
        JsonSerializer.SerializeToElement(dto, dto.GetType(), JsonSerializerOptions.Web);

    private static IReadOnlyList<object> DriverIdentityResponses() =>
    [
        new DriverProfileDto(
            CustomerId, DriverName, null, null, null, [], [], new ThisYearSummaryDto(0, 0, 0, 0), null, null),
        new SubsessionResultRowDto(CustomerId, DriverName, 1, 1, 60, 61, 0, 0, 0, 1, 0, 0),
        new DriverLapsDto(1, CustomerId, 60, 0, 60, 0, []),
        new SeasonStandingDto(1, CustomerId, DriverName, 1, 1, 0, 0, 0, 0, 1, 0),
        new SeasonTtStandingDto(1, CustomerId, DriverName, 1, 1000, 1, 0, 0, 0, 0, 1, 0),
        new SeasonQualifyResultDto(1, CustomerId, DriverName, 1, 2000, 60, 1),
        new GlobalLeaderboardEntryDto(1, 1, CustomerId, DriverName, "US", 1, 0, 2000, 1000, 0),
        new RivalDto(CustomerId, DriverName, DateTimeOffset.UnixEpoch),
        new DriverSearchResultDto(CustomerId, DriverName),
        new RivalSuggestionDto(CustomerId, DriverName, 1),
        new ComparisonSideDto(CustomerId, DriverName, null, null, null, [], [], []),
    ];

    private static IReadOnlyList<object> DriverNameResponses() =>
        DriverIdentityResponses().Where(dto => dto is not DriverLapsDto).ToList();
}
