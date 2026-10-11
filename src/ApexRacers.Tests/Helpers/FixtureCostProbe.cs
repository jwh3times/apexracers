using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace ApexRacers.Tests.Helpers;

/// <summary>Opt-in cost evidence; never supplies privacy acceptance or changes test outcomes.</summary>
internal static class FixtureCostProbe
{
    private static readonly AsyncLocal<Span?> Current = new();
    private static readonly object WriteLock = new();
    private static readonly string Suffix = Guid.NewGuid().ToString("N");
    private static long nextId;

    public static IDisposable Begin(string phase)
    {
        var directory = Environment.GetEnvironmentVariable("APEXRACERS_COST_PROBE_DIR");
        if (string.IsNullOrEmpty(directory)) return Noop.Instance;
        return BeginAt(directory, phase);
    }

    internal static IDisposable BeginAt(string? directory, string phase, Action<string>? invalidDiagnostic = null)
    {
        if (string.IsNullOrEmpty(directory)) return Noop.Instance;
        var span = new Span(directory, phase, Current.Value, invalidDiagnostic);
        Current.Value = span;
        return span;
    }

    public static async Task MeasureAsync(string phase, Func<Task> action)
    {
        using var span = Begin(phase);
        try { await action(); SetOutcome(span, "success"); }
        catch (OperationCanceledException) { SetOutcome(span, "cancel"); throw; }
        catch { SetOutcome(span, "fault"); throw; }
    }

    public static void ProcessResource(int processId)
    {
        if (processId <= 0) { Console.Error.WriteLine("FIXTURE_COST_PROBE_INVALID ProcessResource"); return; }
        Resource("process", processId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public static void ContainerResource(string containerId, int port, string lifecycle)
    {
        if (containerId.Length != 64 || containerId.Any(c => !char.IsAsciiHexDigit(c)))
        { Console.Error.WriteLine("FIXTURE_COST_PROBE_INVALID ContainerResource"); return; }
        var directory = Environment.GetEnvironmentVariable("APEXRACERS_COST_PROBE_DIR");
        if (!string.IsNullOrEmpty(directory))
            Write(directory, new { schemaVersion = 1, processId = Environment.ProcessId,
                eventType = "resource", kind = "container", id = containerId, port, lifecycle,
                ticks = Stopwatch.GetTimestamp(), frequency = Stopwatch.Frequency, utc = DateTimeOffset.UtcNow });
    }

    private static void Resource(string kind, string id)
    {
        var directory = Environment.GetEnvironmentVariable("APEXRACERS_COST_PROBE_DIR");
        if (!string.IsNullOrEmpty(directory))
            Write(directory, new { schemaVersion = 1, processId = Environment.ProcessId,
                eventType = "resource", kind, id, ticks = Stopwatch.GetTimestamp(), frequency = Stopwatch.Frequency, utc = DateTimeOffset.UtcNow });
    }

    private static void Write(string directory, object value, Action<string>? invalidDiagnostic = null)
    {
        // Probe failure invalidates measurement, without hiding an original test failure.
        try
        {
            var line = JsonSerializer.Serialize(value);
            lock (WriteLock)
            {
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, $"phases-{Environment.ProcessId}-{Suffix}.jsonl"), line + "\n");
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            var diagnostic = "FIXTURE_COST_PROBE_INVALID " + error.GetType().Name;
            if (invalidDiagnostic is null) Console.Error.WriteLine(diagnostic);
            else invalidDiagnostic(diagnostic);
        }
    }

    private static void SetOutcome(IDisposable scope, string outcome)
    {
        if (scope is Span span) span.Outcome = outcome;
    }

    private sealed class Span(string directory, string phase, Span? parent, Action<string>? invalidDiagnostic) : IDisposable
    {
        public long Id { get; } = Interlocked.Increment(ref nextId);
        private readonly long start = Stopwatch.GetTimestamp();
        private readonly DateTimeOffset utcStart = DateTimeOffset.UtcNow;
        private readonly string? caseId = TestContext.Current.Test?.UniqueID;
        private readonly string? methodName = TestContext.Current.TestMethod is { } method
            ? method.TestClass.TestClassName + "." + method.MethodName : null;
        public string Outcome { get; set; } = "completed";
        private bool disposed;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            var end = Stopwatch.GetTimestamp();
            Current.Value = parent;
            Write(directory, new
            {
                schemaVersion = 1,
                eventType = "span",
                processId = Environment.ProcessId,
                spanId = Id,
                parentSpanId = parent?.Id,
                caseId,
                methodName,
                phase,
                startTicks = start,
                endTicks = end,
                frequency = Stopwatch.Frequency,
                elapsedSeconds = (end - start) / (double)Stopwatch.Frequency,
                utcStart,
                outcome = Outcome,
            }, invalidDiagnostic);
        }
    }

    private sealed class Noop : IDisposable
    {
        public static readonly Noop Instance = new();
        public void Dispose() { }
    }
}
