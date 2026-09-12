using ApexRacers.Api.Services.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ApexRacers.Tests.Services;

/// <summary>
/// The hand-off that keeps the sign-in security notice out of the response.
/// </summary>
public class OutboundEmailQueueTests
{
    private static OutboundEmail Message(string subject) =>
        new("driver@example.com", null, subject, "<p>body</p>", "body");

    private static OutboundEmailQueue NewQueue() =>
        new(NullLogger<OutboundEmailQueue>.Instance);

    [Fact]
    public async Task Enqueue_MakesTheMessageReadable()
    {
        var queue = NewQueue();

        queue.Enqueue(Message("first"));
        queue.Enqueue(Message("second"));

        Assert.Equal("first", (await queue.Reader.ReadAsync(TestContext.Current.CancellationToken)).Subject);
        Assert.Equal("second", (await queue.Reader.ReadAsync(TestContext.Current.CancellationToken)).Subject);
    }

    /// <summary>
    /// Enqueueing must never throw. It runs on a request that is already refusing a sign-in, and an
    /// exception there would answer that caller differently from one whose address has no account —
    /// the account oracle this whole path exists to avoid.
    /// </summary>
    [Fact]
    public void Enqueue_WhenFull_DropsInsteadOfThrowing()
    {
        var queue = NewQueue();

        var ex = Record.Exception(() =>
        {
            // Well past the bounded capacity.
            for (var i = 0; i < 5000; i++)
                queue.Enqueue(Message($"message-{i}"));
        });

        Assert.Null(ex);
    }

    [Fact]
    public void Enqueue_WhenFull_KeepsTheEarliestMessages()
    {
        var queue = NewQueue();

        for (var i = 0; i < 5000; i++)
            queue.Enqueue(Message($"message-{i}"));

        // DropWrite: the queue keeps what it already accepted rather than evicting it, so the first
        // warning about an attack survives the flood that follows it.
        Assert.True(queue.Reader.TryRead(out var first));
        Assert.Equal("message-0", first!.Subject);
    }
}
