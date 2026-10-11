using System.Security.Cryptography;
using System.Text.Json;

namespace ApexRacers.Tests.PrivacyContract;

/// <summary>Consumes the recorded maintainer decision for the unchanged fabricated template.
/// This test-only receipt neither reviews new risk nor supplies proof, consent or Live permission.</summary>
internal static class CatalogAdmission
{
    public const string ReviewVersion = "catalog30-technical-review-v1";
    public static string RepositoryRoot
    {
        get
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root.Parent is not null && !File.Exists(Path.Combine(root.FullName, "global.json"))) root = root.Parent;
            return root.FullName;
        }
    }

    public static JsonElement Verify()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot, "docs/testing/driver-catalog-admission-v1.json")));
        var receipt = document.RootElement;
        if (receipt.GetProperty("reviewVersion").GetString() != ReviewVersion || receipt.GetProperty("provenance").GetString() != "Demo")
            throw new InvalidOperationException("Recorded synthetic catalog admission is unavailable.");
        foreach (var source in receipt.GetProperty("reviewedSources").EnumerateObject())
        {
            var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(RepositoryRoot, source.Name)).Replace("\r\n", "\n", StringComparison.Ordinal))));
            if (!hash.Equals(source.Value.GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Material template change requires renewed catalog review.");
        }
        return receipt.Clone();
    }

    public static string OutputDirectory(string category)
    {
        var destination = Environment.GetEnvironmentVariable("DRIVER_PRIVACY_EVIDENCE_DIR");
        return Path.Combine(destination ?? Path.Combine(RepositoryRoot, "TestResults"), category);
    }
}
