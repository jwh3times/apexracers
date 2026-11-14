using ApexRacers.Api.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ApexRacers.Tests.Services;

public class SpanExportSourcesTests
{
    private static SpanExportSources For(params (string Category, string Level)[] levels) =>
        SpanExportSources.FromLogLevels(new ConfigurationBuilder()
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

        Assert.Equal(new SpanExportSources(Requests: false, OutgoingHttp: false, Database: false), spans);
    }

    [Fact]
    public void Raising_one_category_to_debug_turns_on_only_its_spans()
    {
        var spans = For(
            ("Default", "Information"),
            ("Microsoft.AspNetCore", "Warning"),
            ("Npgsql", "Debug"));

        Assert.Equal(new SpanExportSources(Requests: false, OutgoingHttp: false, Database: true), spans);
    }

    // Default=Debug is the routine knob for chasing an app bug. It must change log verbosity only,
    // never quietly widen what leaves the process.
    [Fact]
    public void Default_at_debug_exports_no_spans()
    {
        Assert.False(For(("Default", "Debug")).Any);
    }

    [Fact]
    public void A_category_below_debug_keeps_its_spans_off_whatever_default_says()
    {
        var spans = For(("Default", "Trace"), ("Microsoft.AspNetCore", "Warning"), ("Npgsql", "Debug"));

        Assert.Equal(new SpanExportSources(Requests: false, OutgoingHttp: false, Database: true), spans);
    }

    [Fact]
    public void A_parent_category_applies_to_the_source_beneath_it()
    {
        Assert.Equal(new SpanExportSources(false, OutgoingHttp: true, false), For(("System", "Trace")));
    }

    [Fact]
    public void No_logging_configuration_exports_no_spans()
    {
        Assert.False(For().Any);
    }
}
