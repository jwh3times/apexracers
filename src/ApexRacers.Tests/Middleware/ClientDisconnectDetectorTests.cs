using ApexRacers.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace ApexRacers.Tests.Middleware;

public class ClientDisconnectDetectorTests
{
    [Fact]
    public void OperationCanceled_WithRequestAborted_IsDisconnect() =>
        Assert.True(ClientDisconnectDetector.IsClientDisconnect(
            new OperationCanceledException("The operation was canceled."), requestAborted: true));

    [Fact]
    public void TaskCanceled_WithRequestAborted_IsDisconnect() =>
        // TaskCanceledException derives from OperationCanceledException; awaiting an
        // aborted request surfaces either one depending on where it unwinds.
        Assert.True(ClientDisconnectDetector.IsClientDisconnect(
            new TaskCanceledException(), requestAborted: true));

    [Fact]
    public void NpgsqlStyleCancellation_WithRequestAborted_IsDisconnect()
    {
        // Npgsql surfaces a token-triggered query cancellation as an
        // OperationCanceledException wrapping PostgresException 57014 — this is the
        // shape behind the "Query was cancelled" entries seen in the API logs.
        var ex = new OperationCanceledException(
            "Query was cancelled", new Exception("57014: canceling statement due to user request"));

        Assert.True(ClientDisconnectDetector.IsClientDisconnect(ex, requestAborted: true));
    }

    [Fact]
    public void BadHttpRequest_WithRequestAborted_IsDisconnect() =>
        // Kestrel's "Unexpected end of request content." — the client stopped sending
        // the body mid-flight.
        Assert.True(ClientDisconnectDetector.IsClientDisconnect(
            new BadHttpRequestException("Unexpected end of request content."), requestAborted: true));

    [Fact]
    public void KestrelBadHttpRequestException_DerivesFromTheAbstractionsType() =>
        // The detector matches on Microsoft.AspNetCore.Http.BadHttpRequestException, but
        // production throws the Kestrel subclass. If that hierarchy ever changes, the
        // detector silently stops matching the real disconnect — pin it here.
        Assert.True(typeof(BadHttpRequestException).IsAssignableFrom(
            typeof(Microsoft.AspNetCore.Server.Kestrel.Core.BadHttpRequestException)));

    [Fact]
    public void OperationCanceled_WithoutRequestAborted_IsNotDisconnect() =>
        // A server-side timeout or internal cancellation is a real fault: the client is
        // still waiting and deserves a 500, not a silent 499.
        Assert.False(ClientDisconnectDetector.IsClientDisconnect(
            new OperationCanceledException(), requestAborted: false));

    [Fact]
    public void BadHttpRequest_WithoutRequestAborted_IsNotDisconnect() =>
        // A genuinely malformed request (e.g. body too large) also throws
        // BadHttpRequestException, but the connection is alive — keep the 400/500 path.
        Assert.False(ClientDisconnectDetector.IsClientDisconnect(
            new BadHttpRequestException("Request body too large."), requestAborted: false));

    [Fact]
    public void OrdinaryException_WithRequestAborted_IsNotDisconnect() =>
        // The client going away does not excuse an unrelated bug that threw first.
        Assert.False(ClientDisconnectDetector.IsClientDisconnect(
            new InvalidOperationException("real bug"), requestAborted: true));

    [Fact]
    public void ClientClosedRequestStatus_Is499() =>
        Assert.Equal(499, ClientDisconnectDetector.StatusClientClosedRequest);
}
