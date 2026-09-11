using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;

namespace ApexRacers.Api.Services.Email;

/// <summary>
/// Hands an email off to be sent after the current response, rather than inside it.
/// </summary>
/// <remarks>
/// <para>
/// Most account email is fine to send inline: registration, password reset and email-change all
/// already cost the caller a send on every branch, so the time it takes reveals nothing. Sign-in is
/// the exception. Its whole contract is that an address with an account and an address without one
/// are indistinguishable, and an inline send on the failed-sign-in path costs hundreds of
/// milliseconds for real accounts and nothing at all for the rest — an oracle a single request can
/// read off the clock, on the one endpoint where that matters most (GHSA-28pc-cx5w-g6jp).
/// </para>
/// <para>
/// Queueing also decouples the response from the mail provider being healthy. Sent inline, a
/// provider error would propagate and answer the caller with a 500 — again only for addresses that
/// have an account, which is the same oracle wearing a status code.
/// </para>
/// </remarks>
public interface IOutboundEmailQueue
{
    /// <summary>Accepts an email for sending. Returns immediately and never throws.</summary>
    void Enqueue(OutboundEmail email);
}

/// <summary>
/// Channel-backed queue drained by <see cref="OutboundEmailDispatcher"/>.
/// </summary>
/// <remarks>
/// Bounded, and full means drop. An unbounded queue on a path any anonymous caller can reach is a
/// memory-growth lever; the capacity is far above what the notice pacing can actually produce (one
/// message per account per interval), so reaching it means something is already badly wrong and
/// losing a warning email is the better failure.
/// </remarks>
public sealed class OutboundEmailQueue(ILogger<OutboundEmailQueue> logger) : IOutboundEmailQueue
{
    private const int Capacity = 1000;

    private readonly Channel<OutboundEmail> _channel =
        Channel.CreateBounded<OutboundEmail>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });

    public ChannelReader<OutboundEmail> Reader => _channel.Reader;

    public void Enqueue(OutboundEmail email)
    {
        if (!_channel.Writer.TryWrite(email))
            logger.LogWarning("Outbound email queue is full; dropped message (subject {Subject}).", email.Subject);
    }
}

/// <summary>Drains <see cref="OutboundEmailQueue"/>, one message at a time, outside any request.</summary>
[ExcludeFromCodeCoverage] // I/O orchestration shell; queueing and template logic are tested directly
public sealed class OutboundEmailDispatcher(
    OutboundEmailQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<OutboundEmailDispatcher> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var email in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                // Its own scope: the request that queued this is long gone, and so is its scope.
                using var scope = scopeFactory.CreateScope();
                var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
                await sender.SendAsync(email, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Never rethrow: a failed send must not take the dispatcher down, and there is no
                // caller left to tell anyway.
                logger.LogError(ex, "Failed to send queued email (subject {Subject}).", email.Subject);
            }
        }
    }
}
