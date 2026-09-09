using System.Text.Json;
using ApexRacers.Api.Services.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ApexRacers.Tests.Services;

public class FileDropEmailSenderTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"apexracers-maildrop-{Guid.NewGuid():N}");

    private readonly FixedTimeProvider _time =
        new(new DateTimeOffset(2026, 3, 4, 5, 6, 7, 890, TimeSpan.Zero));

    private FileDropEmailSender BuildSender() =>
        new(_directory, _time, NullLogger<FileDropEmailSender>.Instance);

    private static OutboundEmail SampleEmail(string to = "driver@example.com") =>
        new(to, "Driver", "Reset your ApexRacers password", "<p>link</p>", "link");

    [Fact]
    public async Task SendAsync_CreatesTheDirectoryWhenItDoesNotExist()
    {
        Assert.False(Directory.Exists(_directory));

        await BuildSender().SendAsync(SampleEmail(), TestContext.Current.CancellationToken);

        Assert.True(Directory.Exists(_directory));
    }

    [Fact]
    public async Task SendAsync_WritesTheWholeEmailAsJson()
    {
        var email = new OutboundEmail(
            "driver@example.com", "Driver", "Reset your ApexRacers password",
            "<a href=\"https://apexracers.gg/reset-password?token=abc\">Reset</a>",
            "https://apexracers.gg/reset-password?token=abc");

        await BuildSender().SendAsync(email, TestContext.Current.CancellationToken);

        var file = Assert.Single(Directory.GetFiles(_directory));
        var dropped = JsonSerializer.Deserialize<DroppedEmail>(
            await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(dropped);
        Assert.Equal(email.To, dropped!.To);
        Assert.Equal(email.ToName, dropped.ToName);
        Assert.Equal(email.Subject, dropped.Subject);
        Assert.Equal(email.HtmlBody, dropped.HtmlBody);
        // The link is the whole point of the drop: the E2E suite reads the token back out of it.
        Assert.Equal(email.TextBody, dropped.TextBody);
        Assert.Equal(_time.GetUtcNow(), dropped.DroppedAt);
    }

    [Fact]
    public async Task SendAsync_NamesFilesWithASortableUtcTimestamp()
    {
        await BuildSender().SendAsync(SampleEmail(), TestContext.Current.CancellationToken);

        var name = Path.GetFileName(Assert.Single(Directory.GetFiles(_directory)));

        Assert.StartsWith("20260304T050607.890Z-", name);
        Assert.EndsWith(".json", name);
    }

    [Fact]
    public async Task SendAsync_SameMillisecond_WritesSeparateFiles()
    {
        // Parallel E2E workers can drop two emails inside one millisecond; a timestamp-only name
        // would have the second overwrite the first and lose a token the suite is waiting for.
        var sender = BuildSender();

        await sender.SendAsync(SampleEmail("one@example.com"), TestContext.Current.CancellationToken);
        await sender.SendAsync(SampleEmail("two@example.com"), TestContext.Current.CancellationToken);

        Assert.Equal(2, Directory.GetFiles(_directory).Length);
    }

    [Fact]
    public async Task SendAsync_ExistingDirectory_AppendsWithoutClearingIt()
    {
        Directory.CreateDirectory(_directory);
        var stale = Path.Combine(_directory, "stale.json");
        await File.WriteAllTextAsync(stale, "{}", TestContext.Current.CancellationToken);

        await BuildSender().SendAsync(SampleEmail(), TestContext.Current.CancellationToken);

        Assert.True(File.Exists(stale));
        Assert.Equal(2, Directory.GetFiles(_directory).Length);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }
}
