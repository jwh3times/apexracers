using ApexRacers.Api.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ApexRacers.Tests.Services;

public class TelemetrySpansTests
{
    private static TelemetrySpans For(params (string Category, string Level)[] levels) =>
        TelemetrySpans.FromLogLevels(new ConfigurationBuilder()
            .AddInMemoryCollection(levels.Select(l =>
                new KeyValuePair<string, string?>($"Logging:LogLevel:{l.Category}", l.Level)))
            .Build());

    [Fact]
    public void Production_logging_levels_export_no_spans()
    {
        var spans = For(
            ("Default", "Information"),
            ("Microsoft.AspNetCore", "Warning"),
            ("Microsoft.EntityFrameworkCore", "Warning"));

        Assert.Equal(new TelemetrySpans(Requests: false, OutgoingHttp: false, Database: false), spans);
    }

    [Fact]
    public void Raising_one_category_to_debug_turns_on_only_its_spans()
    {
        var spans = For(
            ("Default", "Information"),
            ("Microsoft.AspNetCore", "Warning"),
            ("Npgsql", "Debug"));

        Assert.Equal(new TelemetrySpans(Requests: false, OutgoingHttp: false, Database: true), spans);
    }

    [Fact]
    public void Default_at_debug_turns_on_every_source()
    {
        Assert.Equal(new TelemetrySpans(true, true, true), For(("Default", "Debug")));
    }

    [Fact]
    public void A_more_specific_category_beats_default()
    {
        var spans = For(("Default", "Debug"), ("Microsoft.AspNetCore", "Warning"));

        Assert.Equal(new TelemetrySpans(Requests: false, OutgoingHttp: true, Database: true), spans);
    }

    [Fact]
    public void A_parent_category_applies_to_the_source_beneath_it()
    {
        Assert.Equal(new TelemetrySpans(false, OutgoingHttp: true, false), For(("System", "Trace")));
    }

    [Fact]
    public void No_logging_configuration_exports_no_spans()
    {
        Assert.False(For().Any);
    }
}
