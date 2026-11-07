using System.Security.Cryptography;
using ApexRacers.Core;
using ApexRacers.Data;
using ApexRacers.Data.IndependentJournal;
using ApexRacers.Tests.Helpers;
using Azure.Storage.Blobs;
using Xunit;

namespace ApexRacers.Tests.IndependentJournal;

/// <summary>The production independent journal and publication history over a blob-storage
/// emulator: write-once records, idempotent retries and fail-closed reads.</summary>
public sealed class BlobDriverJournalTests(AzuriteFixture storage) : IClassFixture<AzuriteFixture>
{
    private static readonly DateTimeOffset Loss = DriverAuthorizationPolicy.DurableTime(new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Appended_intent_is_the_scopes_pending_veto()
    {
        var (journal, _) = await NewJournalAsync();
        var scope = new DriverScope(Guid.NewGuid(), 4242, DataProvenance.Real);
        var intent = Intent(scope, DriverLifecycleKind.WithdrawPersonal);

        Assert.Equal(intent, await journal.AppendAsync(intent, Ct));

        var state = await journal.ReadAsync(scope, Ct);
        Assert.True(state.Available);
        Assert.Equal([intent], state.PendingIntents);
        Assert.False(state.Allows(1, DriverConsentScope.Personal));
        var unrelated = await journal.ReadAsync(scope with { CustomerId = 4243 }, Ct);
        Assert.True(unrelated.Available);
        Assert.Empty(unrelated.PendingIntents);
    }

    [Fact]
    public async Task Reappending_an_operation_returns_the_original_record_and_clock()
    {
        var (journal, _) = await NewJournalAsync();
        var scope = new DriverScope(Guid.NewGuid(), 4242, DataProvenance.Real);
        var original = Intent(scope, DriverLifecycleKind.Unlink);
        await journal.AppendAsync(original, Ct);

        var replay = await journal.AppendAsync(original with { OriginalLossAt = Loss.AddHours(3) }, Ct);

        Assert.Equal(original, replay);
        Assert.Equal([original], (await journal.ReadAsync(scope, Ct)).PendingIntents);
    }

    [Fact]
    public async Task Reusing_an_operation_for_a_different_intent_is_refused()
    {
        var (journal, _) = await NewJournalAsync();
        var scope = new DriverScope(Guid.NewGuid(), 4242, DataProvenance.Real);
        var original = Intent(scope, DriverLifecycleKind.Unlink);
        await journal.AppendAsync(original, Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            journal.AppendAsync(original with { Kind = DriverLifecycleKind.WithdrawSharing }, Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            journal.AppendAsync(original with { GrantId = Guid.NewGuid() }, Ct));
        Assert.Equal([original], (await journal.ReadAsync(scope, Ct)).PendingIntents);
    }

    [Fact]
    public async Task Reconciling_clears_the_veto_and_raises_the_minimum_revision()
    {
        var (journal, _) = await NewJournalAsync();
        var scope = new DriverScope(Guid.NewGuid(), 4242, DataProvenance.Real);
        var first = Intent(scope, DriverLifecycleKind.WithdrawSharing);
        var second = Intent(scope, DriverLifecycleKind.WithdrawPersonal);
        await journal.AppendAsync(first, Ct);
        await journal.AppendAsync(second, Ct);

        await journal.ReconcileAsync(first, 3, Ct);
        await journal.ReconcileAsync(first, 3, Ct);

        var state = await journal.ReadAsync(scope, Ct);
        Assert.Equal(3, state.MinimumRevision);
        Assert.Equal([second], state.PendingIntents);
        await journal.ReconcileAsync(second, 5, Ct);
        state = await journal.ReadAsync(scope, Ct);
        Assert.Equal(5, state.MinimumRevision);
        Assert.Empty(state.PendingIntents);
        Assert.False(state.Allows(4, DriverConsentScope.Personal));
        Assert.True(state.Allows(5, DriverConsentScope.Personal));
    }

    [Fact]
    public async Task Reconciling_an_intent_that_was_never_appended_is_refused()
    {
        var (journal, _) = await NewJournalAsync();
        var scope = new DriverScope(Guid.NewGuid(), 4242, DataProvenance.Real);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            journal.ReconcileAsync(Intent(scope, DriverLifecycleKind.Unlink), 1, Ct));
        Assert.Equal(0, (await journal.ReadAsync(scope, Ct)).MinimumRevision);
    }

    [Fact]
    public async Task A_deletion_vetoes_every_customer_id_of_the_user_even_after_reconciliation()
    {
        var (journal, _) = await NewJournalAsync();
        var user = Guid.NewGuid();
        var linked = new DriverScope(user, 4242, DataProvenance.Real);
        var later = Intent(linked, DriverLifecycleKind.DeleteUser, Loss.AddHours(1));
        var earlier = Intent(linked with { CustomerId = 4243 }, DriverLifecycleKind.DeleteUser, Loss);
        await journal.AppendAsync(later, Ct);
        await journal.AppendAsync(earlier, Ct);
        await journal.AppendAsync(Intent(new DriverScope(Guid.NewGuid(), 4242, DataProvenance.Real), DriverLifecycleKind.Unlink), Ct);
        await journal.ReconcileAsync(later, 9, Ct);
        await journal.ReconcileAsync(earlier, 9, Ct);

        var enforcement = await journal.ReadUserAsync(user, Ct);
        Assert.True(enforcement.Available);
        Assert.Equal(earlier, enforcement.Deletion);
        var neverLinked = await journal.ReadCurrentAsync(linked with { CustomerId = 9999 }, Ct);
        Assert.False(neverLinked.Allows(100, DriverConsentScope.Personal));
        var stranger = await journal.ReadUserAsync(Guid.NewGuid(), Ct);
        Assert.True(stranger.Available);
        Assert.Null(stranger.Deletion);
    }

    [Fact]
    public async Task Without_genesis_the_journal_is_unavailable_rather_than_empty()
    {
        var (journal, _) = await NewJournalAsync(Settings(writeGenesisWhenEmpty: false));
        var scope = new DriverScope(Guid.NewGuid(), 4242, DataProvenance.Real);

        Assert.False((await journal.ReadAsync(scope, Ct)).Available);
        Assert.False((await journal.ReadUserAsync(scope.UserId, Ct)).Available);
        await Assert.ThrowsAsync<InvalidOperationException>(() => journal.AppendAsync(Intent(scope, DriverLifecycleKind.Unlink), Ct));
    }

    [Fact]
    public async Task Genesis_is_never_written_over_existing_records()
    {
        var settings = Settings();
        var (journal, containers) = await NewJournalAsync(settings);
        var scope = new DriverScope(Guid.NewGuid(), 4242, DataProvenance.Real);
        await journal.AppendAsync(Intent(scope, DriverLifecycleKind.DeleteUser), Ct);
        await storage.Raw(containers.History).DeleteBlobAsync("genesis", cancellationToken: Ct);

        var restarted = new BlobDriverJournal(containers.Enforcement, containers.History, settings, TimeProvider.System);

        Assert.False((await restarted.ReadUserAsync(scope.UserId, Ct)).Available);
        Assert.False(await storage.Raw(containers.History).GetBlobClient("genesis").ExistsAsync(Ct));
    }

    [Theory]
    [InlineData("epoch")]
    [InlineData("naming")]
    [InlineData("sealing")]
    public async Task A_different_trust_anchor_reads_as_unavailable(string changed)
    {
        var settings = Settings();
        var (journal, containers) = await NewJournalAsync(settings);
        var scope = new DriverScope(Guid.NewGuid(), 4242, DataProvenance.Real);
        await journal.AppendAsync(Intent(scope, DriverLifecycleKind.DeleteUser), Ct);
        var other = changed switch
        {
            "epoch" => settings with { TrustedEpoch = Guid.NewGuid() },
            "naming" => settings with { NameHmac = RandomNumberGenerator.GetBytes(32) },
            _ => settings with { PayloadAes = RandomNumberGenerator.GetBytes(32) },
        };

        var misconfigured = new BlobDriverJournal(containers.Enforcement, containers.History, other, TimeProvider.System);

        Assert.False((await misconfigured.ReadUserAsync(scope.UserId, Ct)).Available);
        Assert.False((await misconfigured.ReadAsync(scope, Ct)).Available);
        await Assert.ThrowsAsync<InvalidOperationException>(() => misconfigured.AppendAsync(Intent(scope, DriverLifecycleKind.Unlink), Ct));
    }

    [Fact]
    public async Task A_restarted_adapter_reads_the_same_state()
    {
        var settings = Settings();
        var (journal, containers) = await NewJournalAsync(settings);
        var scope = new DriverScope(Guid.NewGuid(), 4242, DataProvenance.Real);
        var intent = Intent(scope, DriverLifecycleKind.WithdrawPersonal);
        await journal.AppendAsync(intent, Ct);

        var restarted = new BlobDriverJournal(containers.Enforcement, containers.History, settings with { WriteGenesisWhenEmpty = false }, TimeProvider.System);

        Assert.Equal([intent], (await restarted.ReadAsync(scope, Ct)).PendingIntents);
    }

    [Fact]
    public async Task A_tampered_or_moved_record_reads_as_unavailable()
    {
        var (journal, containers) = await NewJournalAsync();
        var raw = storage.Raw(containers.Enforcement);
        var victim = new DriverScope(Guid.NewGuid(), 4242, DataProvenance.Real);
        var other = new DriverScope(Guid.NewGuid(), 4243, DataProvenance.Real);
        await journal.AppendAsync(Intent(victim, DriverLifecycleKind.Unlink), Ct);
        var victimName = Assert.Single(await NamesAsync(raw, "intents/"));
        await journal.AppendAsync(Intent(other, DriverLifecycleKind.Unlink), Ct);
        var otherName = Assert.Single((await NamesAsync(raw, "intents/")).Where(n => n != victimName));

        // Moving another User's record under the victim's prefix must not read as the victim's.
        var moved = (await raw.GetBlobClient(otherName).DownloadContentAsync(Ct)).Value.Content;
        await raw.GetBlobClient(victimName[..victimName.LastIndexOf('/')] + "/" + new string('0', 64)).UploadAsync(moved, Ct);
        Assert.False((await journal.ReadAsync(victim, Ct)).Available);

        var bytes = (await raw.GetBlobClient(otherName).DownloadContentAsync(Ct)).Value.Content.ToArray();
        bytes[^1] ^= 0xFF;
        await raw.GetBlobClient(otherName).UploadAsync(BinaryData.FromBytes(bytes), overwrite: true, Ct);
        Assert.False((await journal.ReadAsync(other, Ct)).Available);
    }

    [Fact]
    public async Task A_lost_append_response_resolves_to_the_stored_record()
    {
        var lose = new LoseResponsePolicy(m => m.Request.Method == Azure.Core.RequestMethod.Put && m.Request.Uri.Path.Contains("/intents/"), 1);
        var (journal, _) = await NewJournalAsync(policies: lose);
        var scope = new DriverScope(Guid.NewGuid(), 4242, DataProvenance.Real);
        var intent = Intent(scope, DriverLifecycleKind.WithdrawPersonal);

        Assert.Equal(intent, await journal.AppendAsync(intent, Ct));

        Assert.Equal(1, lose.Lost);
        Assert.Equal([intent], (await journal.ReadAsync(scope, Ct)).PendingIntents);
    }

    [Fact]
    public async Task An_append_whose_outcome_stays_uncertain_fails_and_a_later_retry_confirms_it()
    {
        // Three attempts (the clients allow two retries) all reach storage and all lose their response.
        var lose = new LoseResponsePolicy(m => m.Request.Method == Azure.Core.RequestMethod.Put && m.Request.Uri.Path.Contains("/intents/"), 3);
        var (journal, _) = await NewJournalAsync(policies: lose);
        var scope = new DriverScope(Guid.NewGuid(), 4242, DataProvenance.Real);
        var intent = Intent(scope, DriverLifecycleKind.DeleteUser);

        await Assert.ThrowsAsync<InvalidOperationException>(() => journal.AppendAsync(intent, Ct));
        Assert.Equal(3, lose.Lost);

        // The caller treats the append as not done, yet the veto already holds: uncertainty only restricts.
        Assert.Equal(intent, (await journal.ReadUserAsync(scope.UserId, Ct)).Deletion);
        Assert.Equal(intent, await journal.AppendAsync(intent with { OriginalLossAt = Loss.AddDays(1) }, Ct));
    }

    [Fact]
    public async Task The_adapter_only_ever_creates_absent_blobs_and_stores_no_raw_identifiers()
    {
        var recording = new RecordingPolicy();
        var (journal, containers) = await NewJournalAsync(policies: recording);
        var user = Guid.NewGuid();
        var scope = new DriverScope(user, 987654321, DataProvenance.Real);
        var withdrawal = Intent(scope, DriverLifecycleKind.WithdrawSharing);
        await journal.AppendAsync(withdrawal, Ct);
        await journal.AppendAsync(withdrawal, Ct);
        await journal.ReconcileAsync(withdrawal, 2, Ct);
        await journal.ReconcileAsync(withdrawal, 2, Ct);
        await journal.AppendAsync(Intent(scope, DriverLifecycleKind.DeleteUser), Ct);
        await journal.ReadCurrentAsync(scope, Ct);

        // Genesis, two intent creates, one retried intent create, two reconciliation creates.
        Assert.True(recording.Requests.Count(r => r.Method == "PUT") >= 6);
        Assert.DoesNotContain(recording.Requests, r => r.Method == "DELETE");
        Assert.All(recording.Requests.Where(r => r.Method == "PUT"), r =>
        {
            Assert.Equal("*", r.IfNoneMatch);
            Assert.Equal("BlockBlob", r.BlobType);
            Assert.DoesNotMatch("comp=(metadata|tags|appendblock|block|blocklist)", r.Uri.Query);
        });
        string[] forbidden = [user.ToString("N"), user.ToString("D"), "987654321"];
        foreach (var container in new[] { containers.Enforcement, containers.History })
            await foreach (var item in storage.Raw(container).GetBlobsAsync(
                Azure.Storage.Blobs.Models.BlobTraits.Metadata | Azure.Storage.Blobs.Models.BlobTraits.Tags,
                Azure.Storage.Blobs.Models.BlobStates.None, null, Ct))
            {
                var content = (await storage.Raw(container).GetBlobClient(item.Name).DownloadContentAsync(Ct)).Value.Content.ToString();
                foreach (var value in forbidden)
                {
                    Assert.DoesNotContain(value, item.Name, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain(value, content, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain(item.Metadata, m => m.Value.Contains(value, StringComparison.OrdinalIgnoreCase));
                }
            }
    }

    [Fact]
    public async Task A_reconciliation_whose_intent_is_gone_makes_the_scope_unavailable()
    {
        var (journal, containers) = await NewJournalAsync();
        var scope = new DriverScope(Guid.NewGuid(), 4242, DataProvenance.Real);
        var intent = Intent(scope, DriverLifecycleKind.Unlink);
        await journal.AppendAsync(intent, Ct);
        await journal.ReconcileAsync(intent, 2, Ct);

        await storage.Raw(containers.Enforcement).DeleteBlobAsync(Assert.Single(await NamesAsync(storage.Raw(containers.Enforcement), "intents/")), cancellationToken: Ct);

        Assert.False((await journal.ReadAsync(scope, Ct)).Available);
    }

    [Fact]
    public async Task Reconciling_with_a_conflicting_identity_is_refused()
    {
        var (journal, _) = await NewJournalAsync();
        var intent = Intent(new DriverScope(Guid.NewGuid(), 4242, DataProvenance.Real), DriverLifecycleKind.Unlink);
        await journal.AppendAsync(intent, Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            journal.ReconcileAsync(intent with { Kind = DriverLifecycleKind.RevokeProof }, 2, Ct));
        Assert.Equal([intent], (await journal.ReadAsync(intent.Scope, Ct)).PendingIntents);
    }

    [Fact]
    public async Task A_record_in_an_unknown_format_reads_as_unavailable()
    {
        var (journal, containers) = await NewJournalAsync();
        await journal.ReserveAsync(BlobPublicationHistoryTests.Proposal(), Ct);

        await storage.Raw(containers.History).GetBlobClient($"releases/{2:D19}").UploadAsync(BinaryData.FromString("x"), Ct);

        Assert.False((await journal.ReadAsync(Ct)).Available);
    }

    [Fact]
    public async Task Cancellation_propagates_instead_of_reading_as_unavailable()
    {
        var (journal, _) = await NewJournalAsync();
        var scope = new DriverScope(Guid.NewGuid(), 4242, DataProvenance.Real);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => journal.ReadAsync(scope, cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => journal.ReadUserAsync(scope.UserId, cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => journal.ReadAsync(cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => journal.AppendAsync(Intent(scope, DriverLifecycleKind.Unlink), cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => journal.ReserveAsync(BlobPublicationHistoryTests.Proposal(), cancelled.Token));
    }

    [Fact]
    public async Task Two_adapters_provisioning_genesis_at_once_agree_on_one()
    {
        var settings = Settings();
        var barrier = new BarrierPolicy(m => m.Request.Method == Azure.Core.RequestMethod.Put && m.Request.Uri.Path.EndsWith("/genesis"), 2);
        var containers = await storage.CreateContainersAsync(Ct, barrier);
        var first = new BlobDriverJournal(containers.Enforcement, containers.History, settings, TimeProvider.System);
        var second = new BlobDriverJournal(containers.Enforcement, containers.History, settings, TimeProvider.System);

        var reads = await Task.WhenAll(first.ReadAsync(Ct), second.ReadAsync(Ct));

        Assert.Equal(2, barrier.Arrivals);
        Assert.Equal(1, barrier.Conflicts);
        Assert.All(reads, r => Assert.True(r.Available));
    }

    [Fact]
    public void Construction_refuses_an_unanchored_or_reused_secret_configuration()
    {
        var material = RandomNumberGenerator.GetBytes(32);
        var client = new BlobContainerClient(new Uri("https://journal.example.invalid/c"));

        Assert.Throws<ArgumentException>(() => new BlobDriverJournal(client, client, Settings() with { TrustedEpoch = Guid.Empty }, TimeProvider.System));
        Assert.Throws<ArgumentException>(() => new BlobDriverJournal(client, client, new(material, material, Guid.NewGuid()), TimeProvider.System));
        Assert.Throws<ArgumentException>(() => new BlobDriverJournal(client, client, new(material, new byte[16], Guid.NewGuid()), TimeProvider.System));
    }

    internal static async Task<List<string>> NamesAsync(BlobContainerClient container, string prefix)
    {
        var names = new List<string>();
        await foreach (var item in container.GetBlobsAsync(Azure.Storage.Blobs.Models.BlobTraits.None, Azure.Storage.Blobs.Models.BlobStates.None, prefix, Ct))
            names.Add(item.Name);
        return names;
    }

    internal static DriverLifecycleIntent Intent(DriverScope scope, DriverLifecycleKind kind, DateTimeOffset? lossAt = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), scope, kind, lossAt ?? Loss);

    internal static IndependentJournalSettings Settings(Guid? epoch = null, bool writeGenesisWhenEmpty = true) =>
        new(RandomNumberGenerator.GetBytes(32), RandomNumberGenerator.GetBytes(32), epoch ?? Guid.NewGuid(), writeGenesisWhenEmpty);

    private async Task<(BlobDriverJournal Journal, (BlobContainerClient Enforcement, BlobContainerClient History) Containers)> NewJournalAsync(
        IndependentJournalSettings? settings = null, params Azure.Core.Pipeline.HttpPipelinePolicy[] policies)
    {
        var containers = await storage.CreateContainersAsync(Ct, policies);
        return (new BlobDriverJournal(containers.Enforcement, containers.History, settings ?? Settings(), TimeProvider.System), containers);
    }
}
