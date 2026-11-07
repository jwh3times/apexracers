using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Storage.Blobs;
using Testcontainers.Azurite;
using Testcontainers.Xunit;
using Xunit;
using Xunit.Sdk;

namespace ApexRacers.Tests.Helpers;

/// <summary>
/// One pinned blob-storage emulator per test class. Each call to <see cref="CreateContainersAsync"/>
/// makes a fresh pair of containers, so tests sharing the emulator never see each other's records.
/// The emulator validates request shapes, conditional creates and strongly consistent listing; it
/// does not implement immutability policies, which stay in the operator's provisioning procedure.
/// </summary>
public sealed class AzuriteFixture(IMessageSink messageSink)
    : ContainerFixture<AzuriteBuilder, AzuriteContainer>(messageSink)
{
    protected override AzuriteBuilder Configure() =>
        new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.37.0")
            .WithCommand("--skipApiVersionCheck");

    /// <summary>Creates a fresh enforcement/history container pair and returns clients over it.
    /// Policies, when given, see every request the returned clients send.</summary>
    public async Task<(BlobContainerClient Enforcement, BlobContainerClient History)> CreateContainersAsync(
        CancellationToken ct, params HttpPipelinePolicy[] policies)
    {
        var suffix = Guid.NewGuid().ToString("N")[..20];
        var setup = new BlobServiceClient(Container.GetConnectionString());
        await setup.GetBlobContainerClient($"enforcement-{suffix}").CreateAsync(cancellationToken: ct);
        await setup.GetBlobContainerClient($"history-{suffix}").CreateAsync(cancellationToken: ct);
        var options = new BlobClientOptions();
        options.Retry.MaxRetries = 2;
        options.Retry.Delay = TimeSpan.FromMilliseconds(10);
        foreach (var policy in policies) options.AddPolicy(policy, HttpPipelinePosition.PerRetry);
        var service = new BlobServiceClient(Container.GetConnectionString(), options);
        return (service.GetBlobContainerClient($"enforcement-{suffix}"), service.GetBlobContainerClient($"history-{suffix}"));
    }

    /// <summary>A client with no adapter policies, for inspecting or tampering with stored blobs.</summary>
    public BlobContainerClient Raw(BlobContainerClient container) =>
        new BlobServiceClient(Container.GetConnectionString()).GetBlobContainerClient(container.Name);
}
