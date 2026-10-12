using Xunit;

namespace CoveragePreservationFixture;

public class Cases
{
    [Fact]
    public void A() { Assert.Equal(11, Decision.Route(0)); Assert.Equal(22, Decision.Route(1)); }

    [Fact]
    public void B() { Assert.Equal(22, Decision.Route(1)); Assert.Equal(33, Decision.Route(2)); }

    [Fact]
    public void Union() { Assert.Equal(11, Decision.Route(0)); Assert.Equal(22, Decision.Route(1)); Assert.Equal(33, Decision.Route(2)); }
}
