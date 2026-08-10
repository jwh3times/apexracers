using ApexRacers.Api.Middleware;
using ApexRacers.Api.Services;
using Xunit;

namespace ApexRacers.Tests.Middleware;

public class ExceptionStatusMapperTests
{
    [Fact]
    public void IRacingNotLinked_MapsTo409() =>
        Assert.Equal(409, ExceptionStatusMapper.MapStatusCode(new IRacingNotLinkedException()));

    [Fact]
    public void IRacingNotConfigured_MapsTo503() =>
        Assert.Equal(503, ExceptionStatusMapper.MapStatusCode(new IRacingNotConfiguredException()));

    [Fact]
    public void ArgumentException_MapsTo400() =>
        Assert.Equal(400, ExceptionStatusMapper.MapStatusCode(new ArgumentException("bad")));

    [Fact]
    public void ArgumentNullException_MapsTo400() =>
        Assert.Equal(400, ExceptionStatusMapper.MapStatusCode(new ArgumentNullException("p")));

    [Fact]
    public void InvalidOperationException_MapsTo400() =>
        Assert.Equal(400, ExceptionStatusMapper.MapStatusCode(new InvalidOperationException("nope")));

    [Fact]
    public void KeyNotFoundException_MapsTo404() =>
        Assert.Equal(404, ExceptionStatusMapper.MapStatusCode(new KeyNotFoundException("missing")));

    [Fact]
    public void UnauthorizedAccessException_MapsTo401() =>
        Assert.Equal(401, ExceptionStatusMapper.MapStatusCode(new UnauthorizedAccessException("no")));

    [Fact]
    public void UnmappedException_MapsTo500() =>
        Assert.Equal(500, ExceptionStatusMapper.MapStatusCode(new NotImplementedException()));
}
