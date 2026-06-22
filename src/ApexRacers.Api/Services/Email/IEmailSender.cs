namespace ApexRacers.Api.Services.Email;

/// <summary>Sends transactional account emails. Backed by ACS in production, a no-op logger otherwise.</summary>
public interface IEmailSender
{
    Task SendAsync(OutboundEmail email, CancellationToken ct = default);
}
