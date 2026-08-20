using ApexRacers.Core;
using ApexRacers.Core.Models;
using Xunit;

namespace ApexRacers.Tests.Models;

/// <summary>
/// The choice between a Race Best and an Uploaded Best used to run in three services, each keeping
/// only the winning number. These cases pin both halves of the answer — the lap and the evidence —
/// so a caller cannot reintroduce a comparison that forgets where the lap came from.
/// </summary>
public class PersonalBestTests
{
    [Fact]
    public void Select_NeitherEvidenceProducedALap_IsAbsent()
    {
        Assert.Null(PersonalBest.Select(null, null));
    }

    [Fact]
    public void Select_OnlyARaceBest_IsARaceLap()
    {
        var best = PersonalBest.Select(91.2, null);

        Assert.Equal(new PersonalBest(91.2, LapEvidence.RaceLap), best);
    }

    [Fact]
    public void Select_OnlyAnUploadedBest_IsAnUploadedLap()
    {
        var best = PersonalBest.Select(null, 91.2);

        Assert.Equal(new PersonalBest(91.2, LapEvidence.UploadedLap), best);
    }

    [Fact]
    public void Select_FasterUploadedBest_WinsAndSaysSo()
    {
        var best = PersonalBest.Select(91.2, 90.8);

        Assert.Equal(new PersonalBest(90.8, LapEvidence.UploadedLap), best);
    }

    [Fact]
    public void Select_SlowerUploadedBest_LeavesTheRaceBestInPlace()
    {
        var best = PersonalBest.Select(91.2, 93.0);

        Assert.Equal(new PersonalBest(91.2, LapEvidence.RaceLap), best);
    }

    [Fact]
    public void Select_ExactTie_PrefersTheRaceBest()
    {
        // Equally fast, but only one of the two was set against the Field being ranked. The three
        // original call sites all used a strict "faster than" test, so this is the behaviour they
        // shared — now a stated rule rather than a coincidence of three separate comparisons.
        var best = PersonalBest.Select(91.2, 91.2);

        Assert.Equal(LapEvidence.RaceLap, best.Evidence);
    }

    [Theory]
    [InlineData(null, LapEvidence.RaceLap, 91.2)]
    [InlineData(93.0, LapEvidence.RaceLap, 91.2)]
    [InlineData(91.2, LapEvidence.RaceLap, 91.2)]
    [InlineData(90.8, LapEvidence.UploadedLap, 90.8)]
    public void Select_WithAKnownRaceBest_AgreesWithTheNullableOverload(
        double? uploadedBest, LapEvidence expectedEvidence, double expectedLapSeconds)
    {
        var best = PersonalBest.Select(91.2, uploadedBest);

        Assert.Equal(new PersonalBest(expectedLapSeconds, expectedEvidence), best);
        Assert.Equal(PersonalBest.Select((double?)91.2, uploadedBest), (PersonalBest?)best);
    }
}
