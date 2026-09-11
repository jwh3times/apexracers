using ApexRacers.Api.Services.Email;

namespace ApexRacers.Tests.Helpers;

/// <summary>
/// An <see cref="IOutboundEmailQueue"/> that sends through the given sender straight away.
/// </summary>
/// <remarks>
/// Production queues the sign-in security notice so the send cannot be timed from the response — see
/// <see cref="IOutboundEmailQueue"/>. Tests need the opposite: the message has to be observable the
/// moment the call that queued it returns, or every assertion about it races a background drain.
/// Substituting the queue rather than the sender keeps the sender assertions unchanged.
/// </remarks>
public sealed class ImmediateEmailQueue(IEmailSender sender) : IOutboundEmailQueue
{
    public void Enqueue(OutboundEmail email) => sender.SendAsync(email, CancellationToken.None).GetAwaiter().GetResult();
}
