using System.Linq;
using ApexRacers.Api.Services;
using Xunit;

namespace ApexRacers.Tests.Services;

public class QualifyResultsParserTests
{
    // Mirrors the captured stats/season_qualify_results chunk shape (array of driver rows).
    private const string Chunk = """
    [
      {
        "rank": 1,
        "cust_id": 860419,
        "display_name": "Portu Silva",
        "division": 2,
        "license": { "irating": 7480, "tt_rating": 1234 },
        "best_qual_lap_time": 3754685,
        "week": 0
      },
      {
        "rank": 2,
        "cust_id": 111,
        "display_name": "Second Driver",
        "division": 3,
        "license": { "irating": 5000 },
        "best_qual_lap_time": 3760000,
        "week": 0
      }
    ]
    """;

    [Fact]
    public void Parse_MapsRowFields_AndConvertsLapToSeconds()
    {
        var rows = QualifyResultsParser.Parse([Chunk]);

        Assert.Equal(2, rows.Count);
        var first = rows[0];
        Assert.Equal(1, first.Standing);
        Assert.Equal(860419, first.CustomerId);
        Assert.Equal("Portu Silva", first.DriverName);
        Assert.Equal(2, first.Division);
        Assert.Equal(7480, first.IRating);
        Assert.Equal(375.4685, first.BestQualLapSeconds, precision: 4); // 3754685 / 10000
        Assert.Equal(0, first.RaceWeekIndex);
    }

    [Fact]
    public void Parse_ConcatenatesMultipleChunks()
    {
        var second = """
        [
          { "rank": 3, "cust_id": 222, "display_name": "Third", "division": 1,
            "license": { "irating": 4000 }, "best_qual_lap_time": 3800000, "week": 0 }
        ]
        """;

        var rows = QualifyResultsParser.Parse([Chunk, second]);

        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { 1, 2, 3 }, rows.Select(r => r.Standing));
    }

    [Fact]
    public void Parse_NonPositiveLapTime_MapsToSentinel()
    {
        var json = """
        [
          { "rank": 1, "cust_id": 1, "display_name": "NoLap", "division": 0,
            "license": { "irating": 1000 }, "best_qual_lap_time": 0, "week": 2 }
        ]
        """;

        var rows = QualifyResultsParser.Parse([json]);

        Assert.Equal(QualifyResultsParser.NoLapSentinel, rows[0].BestQualLapSeconds);
        Assert.Equal(2, rows[0].RaceWeekIndex);
    }

    [Fact]
    public void Parse_MissingLicense_LeavesIRatingNull()
    {
        var json = """
        [
          { "rank": 1, "cust_id": 1, "display_name": "NoLicense", "division": 0,
            "best_qual_lap_time": 3700000, "week": 0 }
        ]
        """;

        var rows = QualifyResultsParser.Parse([json]);

        Assert.Null(rows[0].IRating);
    }

    [Fact]
    public void Parse_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(QualifyResultsParser.Parse([]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_BlankChunk_IsSkipped(string blank)
    {
        // A blank/whitespace chunk is skipped without throwing; valid chunks still parse.
        var rows = QualifyResultsParser.Parse([blank, Chunk]);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void Parse_NonArrayJson_IsSkipped()
    {
        // iRacing occasionally returns an object (e.g. an error envelope) instead of an array.
        var rows = QualifyResultsParser.Parse(["""{ "error": "no data" }""", Chunk]);
        Assert.Equal(2, rows.Count);
    }
}
