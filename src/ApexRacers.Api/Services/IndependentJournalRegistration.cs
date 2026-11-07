using System.Text.RegularExpressions;
using ApexRacers.Core;
using ApexRacers.Data;
using Azure.Core;
using Azure.Storage.Blobs;

namespace ApexRacers.Api.Services;

public enum IndependentJournalState { NotConfigured, Incomplete, Configured }

/// <summary>Where the independent journal lives and the material that protects it.</summary>
public sealed record IndependentJournalOptions(Uri Endpoint, string EnforcementContainer, string HistoryContainer,
    IndependentJournalSettings Settings);

/// <summary>The outcome of reading the journal settings. <see cref="Problems"/> names settings,
/// never their values.</summary>
public sealed record IndependentJournalBinding(IndependentJournalState State, IndependentJournalOptions? Options,
    IReadOnlyList<string> Problems);

/// <summary>
/// Selects the production independent journal from configuration, or keeps the unavailable
/// adapters. Only a complete, valid set of settings selects the blob adapter; anything missing or
/// malformed closes affected Driver scopes rather than falling back to primary-database state.
/// No setting can select a synthetic adapter. Only the API host resolves the journal.
/// </summary>
public static partial class IndependentJournalRegistration
{
    public const string EndpointSetting = "INDEPENDENT_JOURNAL_ENDPOINT";
    public const string EnforcementContainerSetting = "INDEPENDENT_JOURNAL_ENFORCEMENT_CONTAINER";
    public const string HistoryContainerSetting = "INDEPENDENT_JOURNAL_HISTORY_CONTAINER";
    public const string NameHmacSetting = "INDEPENDENT_JOURNAL_NAME_HMAC";
    public const string PayloadAesSetting = "INDEPENDENT_JOURNAL_PAYLOAD_AES";
    public const string TrustedEpochSetting = "INDEPENDENT_JOURNAL_TRUSTED_EPOCH";
    public const string WriteGenesisSetting = "INDEPENDENT_JOURNAL_WRITE_GENESIS";

    private static readonly string[] Required =
        [EndpointSetting, EnforcementContainerSetting, HistoryContainerSetting, NameHmacSetting, PayloadAesSetting, TrustedEpochSetting];

    public static IndependentJournalBinding Bind(IConfiguration configuration)
    {
        string? Value(string name) => configuration[name] is { Length: > 0 } v ? v.Trim() : null;
        if (Required.Append(WriteGenesisSetting).All(name => Value(name) is null))
            return new(IndependentJournalState.NotConfigured, null, []);

        var problems = Required.Where(name => Value(name) is null).ToList();
        Uri? endpoint = null;
        if (Value(EndpointSetting) is { } rawEndpoint
            && (!Uri.TryCreate(rawEndpoint, UriKind.Absolute, out endpoint) || endpoint.Scheme != Uri.UriSchemeHttps))
            problems.Add(EndpointSetting);
        foreach (var name in new[] { EnforcementContainerSetting, HistoryContainerSetting })
            if (Value(name) is { } container && !ContainerName().IsMatch(container)) problems.Add(name);
        if (Value(EnforcementContainerSetting) is { } e && e == Value(HistoryContainerSetting)) problems.Add(HistoryContainerSetting);
        var naming = Material(Value(NameHmacSetting));
        var sealing = Material(Value(PayloadAesSetting));
        if (Value(NameHmacSetting) is not null && naming is null) problems.Add(NameHmacSetting);
        if (Value(PayloadAesSetting) is not null && (sealing is null || naming is not null && naming.AsSpan().SequenceEqual(sealing)))
            problems.Add(PayloadAesSetting);
        var epoch = Guid.Empty;
        if (Value(TrustedEpochSetting) is { } rawEpoch && (!Guid.TryParse(rawEpoch, out epoch) || epoch == Guid.Empty))
            problems.Add(TrustedEpochSetting);
        var writeGenesis = false;
        if (Value(WriteGenesisSetting) is { } rawGenesis && !bool.TryParse(rawGenesis, out writeGenesis))
            problems.Add(WriteGenesisSetting);

        if (problems.Count > 0) return new(IndependentJournalState.Incomplete, null, problems.Distinct().ToList());
        return new(IndependentJournalState.Configured, new(endpoint!, Value(EnforcementContainerSetting)!, Value(HistoryContainerSetting)!,
            new IndependentJournalSettings(naming!, sealing!, epoch, writeGenesis)), []);
    }

    public static void Register(IServiceCollection services, IndependentJournalBinding binding, TokenCredential credential)
    {
        if (binding is not { State: IndependentJournalState.Configured, Options: { } options })
        {
            services.AddSingleton<IDriverEnforcementJournal, UnavailableDriverEnforcementJournal>();
            services.AddSingleton<IPublicationHistoryAuthority, UnavailablePublicationHistory>();
            return;
        }
        services.AddSingleton(sp =>
        {
            var account = new BlobServiceClient(options.Endpoint, credential);
            return new BlobDriverJournal(account.GetBlobContainerClient(options.EnforcementContainer),
                account.GetBlobContainerClient(options.HistoryContainer), options.Settings, sp.GetRequiredService<TimeProvider>());
        });
        // One instance answers both: the Driver stores apply history checks only when the journal
        // they hold is also the history authority.
        services.AddSingleton<IDriverEnforcementJournal>(sp => sp.GetRequiredService<BlobDriverJournal>());
        services.AddSingleton<IPublicationHistoryAuthority>(sp => sp.GetRequiredService<BlobDriverJournal>());
    }

    private static byte[]? Material(string? value)
    {
        if (value is null) return null;
        var buffer = new byte[32];
        return Convert.TryFromBase64String(value, buffer, out var written) && written == 32 && value.Length == 44 ? buffer : null;
    }

    [GeneratedRegex("^(?=.{3,63}$)[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex ContainerName();
}
