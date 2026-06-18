using ApexRacers.Api.Services;
using Xunit;

namespace ApexRacers.Tests.Services;

public class LeaderboardCsvParserTests
{
    private const string Header =
        "DRIVER,CUSTID,LOCATION,CLUB_NAME,STARTS,WINS,AVG_START_POS,AVG_FINISH_POS,AVG_POINTS,TOP25PCNT,LAPS,LAPSLEAD,AVG_INC,CLASS,IRATING,TTRATING,TOT_CLUBPOINTS,CHAMPPOINTS";

    private static string Row(string driver, long custId, string loc, int starts, int wins, int irating, int tt, int champ) =>
        $"{driver},{custId},{loc},N/A,{starts},{wins},3,4,150,1200,30000,7800,5.7,A 2.96,{irating},{tt},20000,{champ}";

    [Fact]
    public void Parse_MapsColumnsAndRanksInOrder()
    {
        var csv = string.Join('\n',
            Header,
            Row("Aaron Vazquezz", 598355, "ES", 1919, 694, 11424, 2187, 295527),
            Row("Sven Haase", 313251, "DE", 2403, 1399, 11417, 1721, 440170));

        var rows = LeaderboardCsvParser.Parse(csv, categoryId: 2, topN: 50);

        Assert.Equal(2, rows.Count);

        Assert.Equal(2, rows[0].CategoryId);
        Assert.Equal(1, rows[0].Rank);
        Assert.Equal(598355, rows[0].CustId);
        Assert.Equal("Aaron Vazquezz", rows[0].Driver);
        Assert.Equal("ES", rows[0].Location);
        Assert.Equal(1919, rows[0].Starts);
        Assert.Equal(694, rows[0].Wins);
        Assert.Equal(11424, rows[0].IRating);
        Assert.Equal(2187, rows[0].TtRating);
        Assert.Equal(295527, rows[0].ChampPoints);

        Assert.Equal(2, rows[1].Rank);
        Assert.Equal("Sven Haase", rows[1].Driver);
    }

    [Fact]
    public void Parse_RespectsTopN()
    {
        var csv = string.Join('\n',
            Header,
            Row("A", 1, "US", 1, 1, 9000, 1, 1),
            Row("B", 2, "US", 1, 1, 8000, 1, 1),
            Row("C", 3, "US", 1, 1, 7000, 1, 1));

        var rows = LeaderboardCsvParser.Parse(csv, categoryId: 5, topN: 2);

        Assert.Equal(2, rows.Count);
        Assert.Equal("A", rows[0].Driver);
        Assert.Equal("B", rows[1].Driver);
    }

    [Fact]
    public void Parse_SkipsMalformedRows()
    {
        var csv = string.Join('\n',
            Header,
            "too,few,columns",
            Row("Valid Driver", 42, "GB", 10, 5, 5000, 1300, 9999));

        var rows = LeaderboardCsvParser.Parse(csv, categoryId: 1, topN: 50);

        var only = Assert.Single(rows);
        Assert.Equal("Valid Driver", only.Driver);
        Assert.Equal(1, only.Rank); // rank only advances for valid rows
    }

    [Fact]
    public void Parse_HandlesCarriageReturnsAndBlankInput()
    {
        Assert.Empty(LeaderboardCsvParser.Parse(null, 2, 50));
        Assert.Empty(LeaderboardCsvParser.Parse("   ", 2, 50));

        var csv = $"{Header}\r\n{Row("Win", 7, "AU", 1, 1, 6000, 1, 1)}\r\n";
        var rows = LeaderboardCsvParser.Parse(csv, 2, 50);
        Assert.Equal("Win", Assert.Single(rows).Driver);
    }
}
