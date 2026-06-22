using ApexRacers.Api.Services.Email;

namespace ApexRacers.Tests.Helpers;

/// <summary>Captures sent emails so service tests can assert delivery without ACS.</summary>
public sealed class FakeEmailSender : IEmailSender
{
    private readonly List<OutboundEmail> _sent = [];
    public IReadOnlyList<OutboundEmail> Sent => _sent;
    public OutboundEmail? Last => _sent.Count > 0 ? _sent[^1] : null;

    public Task SendAsync(OutboundEmail email, CancellationToken ct = default)
    {
        _sent.Add(email);
        return Task.CompletedTask;
    }
}
