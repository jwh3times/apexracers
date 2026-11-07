using System.Security.Cryptography;
using ApexRacers.Api.Services;
using ApexRacers.Core;
using ApexRacers.Data;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using R = ApexRacers.Api.Services.IndependentJournalRegistration;

namespace ApexRacers.Tests.IndependentJournal;

/// <summary>Configuration selects the production independent journal only when every setting is
/// present and valid; anything else keeps the unavailable adapters, which close Driver scopes.</summary>
public sealed class IndependentJournalRegistrationTests
{
    private static readonly string Naming = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private static readonly string Sealing = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static Dictionary<string, string?> Complete() => new()
    {
        [R.EndpointSetting] = "https://journal.example.invalid",
        [R.EnforcementContainerSetting] = "enforcement",
        [R.HistoryContainerSetting] = "publication-history",
        [R.NameHmacSetting] = Naming,
        [R.PayloadAesSetting] = Sealing,
        [R.TrustedEpochSetting] = "6f1d2c3b-4a59-4e6f-8a7b-9c0d1e2f3a4b",
    };

    [Fact]
    public void A_complete_configuration_registers_one_adapter_as_both_authorities()
    {
        var (provider, binding) = Build(Complete());

        Assert.Equal(IndependentJournalState.Configured, binding.State);
        var journal = Assert.IsType<BlobDriverJournal>(provider.GetRequiredService<IDriverEnforcementJournal>());
        Assert.Same(journal, provider.GetRequiredService<IPublicationHistoryAuthority>());
        Assert.Equal(Guid.Parse("6f1d2c3b-4a59-4e6f-8a7b-9c0d1e2f3a4b"), binding.Options!.Settings.TrustedEpoch);
        Assert.False(binding.Options.Settings.WriteGenesisWhenEmpty);
    }

    [Fact]
    public void No_configuration_keeps_the_unavailable_adapters_quietly()
    {
        var (provider, binding) = Build([]);

        Assert.Equal(IndependentJournalState.NotConfigured, binding.State);
        Assert.Empty(binding.Problems);
        AssertUnavailable(provider);
    }

    [Theory]
    [InlineData(R.EndpointSetting)]
    [InlineData(R.EnforcementContainerSetting)]
    [InlineData(R.HistoryContainerSetting)]
    [InlineData(R.NameHmacSetting)]
    [InlineData(R.PayloadAesSetting)]
    [InlineData(R.TrustedEpochSetting)]
    public void Any_missing_setting_keeps_the_unavailable_adapters(string missing)
    {
        var values = Complete();
        values.Remove(missing);

        var (provider, binding) = Build(values);

        Assert.Equal(IndependentJournalState.Incomplete, binding.State);
        Assert.Contains(missing, binding.Problems);
        AssertUnavailable(provider);
    }

    [Theory]
    [InlineData(R.EndpointSetting, "http://journal.example.invalid")]
    [InlineData(R.EndpointSetting, "not a uri")]
    [InlineData(R.EnforcementContainerSetting, "Enforcement_Records")]
    [InlineData(R.HistoryContainerSetting, "enforcement")]
    [InlineData(R.NameHmacSetting, "c2hvcnQ=")]
    [InlineData(R.PayloadAesSetting, "not base64!")]
    [InlineData(R.TrustedEpochSetting, "00000000-0000-0000-0000-000000000000")]
    [InlineData(R.WriteGenesisSetting, "sometimes")]
    public void Any_invalid_setting_keeps_the_unavailable_adapters(string setting, string value)
    {
        var values = Complete();
        values[setting] = value;

        var (provider, binding) = Build(values);

        Assert.Equal(IndependentJournalState.Incomplete, binding.State);
        Assert.Contains(setting, binding.Problems);
        AssertUnavailable(provider);
    }

    [Fact]
    public void The_same_material_for_names_and_payloads_is_refused()
    {
        var values = Complete();
        values[R.PayloadAesSetting] = Naming;

        var (provider, binding) = Build(values);

        Assert.Contains(R.PayloadAesSetting, binding.Problems);
        AssertUnavailable(provider);
    }

    [Fact]
    public void The_genesis_switch_is_an_explicit_opt_in()
    {
        var values = Complete();
        values[R.WriteGenesisSetting] = "true";

        var (_, binding) = Build(values);

        Assert.True(binding.Options!.Settings.WriteGenesisWhenEmpty);
    }

    [Fact]
    public void Problems_name_settings_and_never_carry_their_values()
    {
        var values = Complete();
        values[R.NameHmacSetting] = "c2hvcnQ=";

        var (_, binding) = Build(values);

        Assert.All(binding.Problems, p =>
        {
            Assert.DoesNotContain("c2hvcnQ=", p);
            Assert.DoesNotContain(Sealing, p);
        });
    }

    private static void AssertUnavailable(ServiceProvider provider)
    {
        Assert.IsType<UnavailableDriverEnforcementJournal>(provider.GetRequiredService<IDriverEnforcementJournal>());
        Assert.IsType<UnavailablePublicationHistory>(provider.GetRequiredService<IPublicationHistoryAuthority>());
    }

    private static (ServiceProvider Provider, IndependentJournalBinding Binding) Build(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var binding = R.Bind(configuration);
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        R.Register(services, binding, new DefaultAzureCredential());
        return (services.BuildServiceProvider(), binding);
    }
}
