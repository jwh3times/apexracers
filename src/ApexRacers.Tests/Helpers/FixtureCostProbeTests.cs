using System.Text.Json;
using Xunit;

namespace ApexRacers.Tests.Helpers;

public sealed class FixtureCostProbeTests
{
    [Fact]
    public void Disabled_scope_performs_no_output()
    {
        var directory = Path.Combine(Path.GetTempPath(), "apexracers_cost_probe_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (FixtureCostProbe.BeginAt(directory, "root"))
            {
                using var scope = FixtureCostProbe.BeginAt(null, "disabled");
                scope.Dispose();
                Assert.Empty(Directory.Exists(directory) ? Directory.GetFiles(directory) : []);
            }
            using var row = JsonDocument.Parse(Assert.Single(File.ReadAllLines(Assert.Single(Directory.GetFiles(directory)))));
            Assert.Equal("root", row.RootElement.GetProperty("phase").GetString());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task Nested_async_scopes_preserve_parent_and_idempotent_disposal_after_error()
    {
        var directory = Path.Combine(Path.GetTempPath(), "apexracers_cost_probe_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (FixtureCostProbe.BeginAt(directory, "root"))
            {
                var child = FixtureCostProbe.BeginAt(directory, "child");
                await Task.Yield();
                child.Dispose();
                child.Dispose();
                try
                {
                    using var failed = FixtureCostProbe.BeginAt(directory, "failed");
                    throw new InvalidOperationException("Original synthetic failure");
                }
                catch (InvalidOperationException) { }
                using var sibling = FixtureCostProbe.BeginAt(directory, "sibling");
            }
            var rows = File.ReadAllLines(Assert.Single(Directory.GetFiles(directory)))
                .Select(line => JsonDocument.Parse(line)).ToArray();
            try
            {
                Assert.Equal(4, rows.Length);
                var root = Assert.Single(rows, row => row.RootElement.GetProperty("phase").GetString() == "root").RootElement;
                Assert.Equal(JsonValueKind.Null, root.GetProperty("parentSpanId").ValueKind);
                foreach (var row in rows.Where(row => row.RootElement.GetProperty("phase").GetString() != "root"))
                {
                    Assert.Equal(root.GetProperty("spanId").GetInt64(), row.RootElement.GetProperty("parentSpanId").GetInt64());
                    Assert.InRange(row.RootElement.GetProperty("startTicks").GetInt64(), root.GetProperty("startTicks").GetInt64(), root.GetProperty("endTicks").GetInt64());
                    Assert.InRange(row.RootElement.GetProperty("endTicks").GetInt64(), row.RootElement.GetProperty("startTicks").GetInt64(), root.GetProperty("endTicks").GetInt64());
                }
                Assert.DoesNotContain("Original synthetic failure", File.ReadAllText(Assert.Single(Directory.GetFiles(directory))));
                var occupiedPath = Path.Combine(directory, "occupied");
                await File.WriteAllTextAsync(occupiedPath, "synthetic occupied path", TestContext.Current.CancellationToken);
                var original = new InvalidOperationException("Original failure must survive probe I/O failure");
                string? diagnostic = null;
                Action failWithProbe = () =>
                {
                    using var failedOutput = FixtureCostProbe.BeginAt(occupiedPath, "failed-output", message => diagnostic = message);
                    throw original;
                };
                var observed = Assert.Throws<InvalidOperationException>(failWithProbe);
                Assert.Same(original, observed);
                Assert.Equal("FIXTURE_COST_PROBE_INVALID IOException", diagnostic);
            }
            finally { foreach (var row in rows) row.Dispose(); }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
}
