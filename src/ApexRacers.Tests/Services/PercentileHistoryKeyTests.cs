using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Core.PercentileHistory;
using Xunit;

namespace ApexRacers.Tests.Services;

public sealed class PercentileHistoryKeyTests
{
    private static readonly PercentileOwnerIdentity Owner = new(Guid.NewGuid(), DemoData.DriverCustId, DataProvenance.Demo, PercentileOwnerKind.DemoSubject);
    private static readonly Guid Week = Guid.NewGuid();
    private static readonly PercentileSelection RaceOnly = PercentileSelection.FromRequest(false, null);

    [Theory]
    [InlineData("v1:0:0")]
    [InlineData("v1:1:63")]
    [InlineData("v1:2:1")]
    [InlineData("v1:2:19")]
    [InlineData("v1:2:63")]
    public void ClassifiedStorageKeyRoundTripsExactReviewedMeaning(string key)
    {
        var selection = PercentileSelection.FromStorage(1, key);
        Assert.Equal(key, selection.Key);
        Assert.Equal(1, selection.PolicyVersion);
        Assert.DoesNotContain((LapSessionType)6, selection.IncludedTypes);
    }

    [Theory]
    [InlineData(0, "v1:0:0")]
    [InlineData(2, "v2:0:0")]
    [InlineData(1, "")]
    [InlineData(1, "v1:01:63")]
    [InlineData(1, "v1:1:64")]
    [InlineData(1, "v1:2:2")]
    [InlineData(1, "v1:2:65")]
    [InlineData(1, "v1:0:1")]
    public void SelectionlessFutureAndNoncanonicalRowsAreNotReinterpreted(int version, string key) =>
        Assert.Throws<ArgumentException>(() => PercentileSelection.FromStorage(version, key));

    [Fact]
    public void CanonicalEquivalencesShareIdentityButDistinctSelectionsAndOwnerScopesNeverDo()
    {
        var key = Key(Owner, RaceOnly);
        Assert.Equal(key, Key(Owner, PercentileSelection.FromRequest(false, [LapSessionType.Practice])));
        Assert.NotEqual(key, Key(Owner, PercentileSelection.FromRequest(true, null)));
        Assert.NotEqual(key, Key(Owner with { UserId = Guid.NewGuid() }, RaceOnly));
        Assert.NotEqual(key, PercentileHistoryKey.Create(Owner, null, RaceOnly, 1, 2, Guid.NewGuid()));
        Assert.NotEqual(key, PercentileHistoryKey.Create(Owner, null, RaceOnly, 2, 2, Week));
        var grant = Guid.NewGuid();
        var personal = Owner with { Kind = PercentileOwnerKind.Personal, CustomerId = 42 };
        var personalKey = PercentileHistoryKey.Create(personal, grant, RaceOnly, 1, 2, Week);
        Assert.NotEqual(key, personalKey);
        Assert.NotEqual(personalKey, PercentileHistoryKey.Create(personal, Guid.NewGuid(), RaceOnly, 1, 2, Week));
        Assert.Equal(64, PercentileHistoryKey.Hash(key).Length);
    }

    [Fact]
    public void NewIdentityCannotBeInventedFromDemoClaimOrMissingPersonalAssociation()
    {
        Assert.Throws<ArgumentException>(() => Key(Owner with { CustomerId = 777 }, RaceOnly));
        Assert.Throws<ArgumentException>(() => Key(Owner with { Provenance = DataProvenance.Real }, RaceOnly));
        Assert.Throws<ArgumentException>(() => Key(Owner with { Kind = PercentileOwnerKind.Personal }, RaceOnly));
        Assert.Throws<ArgumentException>(() => PercentileHistoryKey.Create(Owner, Guid.NewGuid(), RaceOnly, 1, 2, Week));
    }

    private static string Key(PercentileOwnerIdentity owner, PercentileSelection selection) =>
        PercentileHistoryKey.Create(owner, null, selection, 1, 2, Week);
}
