using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApexRacers.Api.Dtos;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ApexRacers.Tests.Lifecycle;

/// <summary>Independent controlled synthetic Personal history evidence; no production adapter,
/// admitted sharing catalog, quarantined upload or stored claim authorizes these responses.</summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class PercentileHistoryHttpTests(PostgreSqlFixture postgres)
{
    [Fact]
    public Task CoherentPersonalHistoryUsesSelectedBestForRankAndMedianAndActualAuthenticatedOwner() =>
        LifecycleTopology.RunAsync(postgres, "percentile-history-owner-selection", ["UPLOAD-01", "AUTH-01", "COPY-03"], async test =>
        {
            await PrepareAsync(test);
            using var upload = await UploadAsync(test, 90);
            upload.EnsureSuccessStatusCode();
            using var response = await test.Publisher.Client.GetAsync("/percentile-history/selected?series=1", test.CancellationToken);
            response.EnsureSuccessStatusCode();
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            var selected = Assert.Single((await response.Content.ReadFromJsonAsync<CarAnalyticsDto[]>(test.CancellationToken))!);
            var reading = Assert.Single(selected.PercentileHistory);
            Assert.Equal(90, reading.PersonalBestLapSeconds);
            Assert.Equal(LapEvidence.UploadedLap, reading.PersonalBestLapEvidence);
            Assert.Equal(90, reading.PercentileRank);
            Assert.Equal(110, reading.MedianLapSeconds);
            Assert.Equal(5, reading.SampleSize);
            var text = await response.Content.ReadAsStringAsync(test.CancellationToken);
            using var json = JsonDocument.Parse(text);
            var wireReading = json.RootElement[0].GetProperty("percentileHistory")[0];
            Assert.Equal("UploadedLap", wireReading.GetProperty("personalBestLapEvidence").GetString());
            Assert.Equal(JsonValueKind.String, wireReading.GetProperty("uploadedSessionType").ValueKind);
            Assert.Equal(reading.UploadedSessionType!.Value.ToString(), wireReading.GetProperty("uploadedSessionType").GetString());
            Assert.DoesNotContain("grantId", text);
            Assert.DoesNotContain("sourceCopyId", text);
            Assert.DoesNotContain("driverName", text);
            await test.WaitAsync("selected", "checkpointed");
            using var race = await test.Publisher.Client.GetAsync("/percentile-history/race?uploaded=false&series=1", test.CancellationToken);
            race.EnsureSuccessStatusCode();
            var raceReading = Assert.Single(Assert.Single((await race.Content.ReadFromJsonAsync<CarAnalyticsDto[]>(test.CancellationToken))!).PercentileHistory);
            Assert.Equal(105, raceReading.PersonalBestLapSeconds);
            Assert.Equal(LapEvidence.RaceLap, raceReading.PersonalBestLapEvidence);
            Assert.Equal(70, raceReading.PercentileRank);
            Assert.NotEqual(reading.SelectionKey, raceReading.SelectionKey);
            await test.WaitAsync("race", "checkpointed");
            using var other = await test.Publisher.Client.GetAsync("/percentile-history/other?actor=other", test.CancellationToken);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, other.StatusCode);
            Assert.Equal("no-store", other.Headers.CacheControl?.ToString());
            await using var db = test.OpenPrimary();
            Assert.Equal(2, await db.ClassifiedCarPercentileResults.CountAsync(test.CancellationToken));
        });

    [Fact]
    public Task NewUploadAtUnsentBoundaryDeniesOldHistoryAndFreshReadUsesCorrectedBest() =>
        LifecycleTopology.RunAsync(postgres, "percentile-history-unsent-source", ["UPLOAD-01", "COPY-03", "HTTP-01"], async test =>
        {
            await PrepareAsync(test);
            await test.HoldAsync("old-history", "admitted");
            var read = test.Publisher.Client.GetAsync("/percentile-history/old-history", test.CancellationToken);
            await test.WaitAsync("old-history", "admitted");
            using var upload = await UploadAsync(test, 85);
            upload.EnsureSuccessStatusCode();
            await test.ReleaseAsync("old-history", "admitted");
            using var denied = await read;
            Assert.Equal(HttpStatusCode.ServiceUnavailable, denied.StatusCode);
            Assert.Equal("no-store", denied.Headers.CacheControl?.ToString());
            Assert.DoesNotContain("percentileRank", await denied.Content.ReadAsStringAsync(test.CancellationToken));
            await test.WaitAsync("old-history", "checkpointed");
            using var current = await test.Publisher.Client.GetAsync("/percentile-history/new-history", test.CancellationToken);
            current.EnsureSuccessStatusCode();
            var row = Assert.Single(Assert.Single((await current.Content.ReadFromJsonAsync<CarAnalyticsDto[]>(test.CancellationToken))!).PercentileHistory);
            Assert.Equal(85, row.PersonalBestLapSeconds);
            await test.WaitAsync("new-history", "checkpointed");
        });

    [Fact]
    public Task EmptyHistoryStillChecksNewPrivateEvidenceBeforeFirstByte()
        => LifecycleTopology.RunAsync(postgres, "percentile-history-empty-unsent", ["UPLOAD-01", "COPY-03", "HTTP-01"], async test =>
        {
            await PrepareAsync(test);
            await using (var db = test.OpenPrimary())
            {
                var fieldPurpose = await db.EvidencePurposes.Where(p => p.Kind == EvidencePurposeKind.IndependentOfficial).Select(p => p.Id).SingleAsync(test.CancellationToken);
                await new EvidenceCopyLifecycle(db, TimeProvider.System).EndPurposeAsync(fieldPurpose, DateTimeOffset.UtcNow, test.CancellationToken);
            }
            using (var empty = await test.Publisher.Client.GetAsync("/percentile-history/empty-current", test.CancellationToken))
            {
                empty.EnsureSuccessStatusCode();
                Assert.Empty((await empty.Content.ReadFromJsonAsync<CarAnalyticsDto[]>(test.CancellationToken))!);
                await test.WaitAsync("empty-current", "checkpointed");
            }
            await test.HoldAsync("empty-prepared", "admitted");
            var read = test.Publisher.Client.GetAsync("/percentile-history/empty-prepared", test.CancellationToken);
            await test.WaitAsync("empty-prepared", "admitted");
            using var upload = await UploadAsync(test, 85);
            upload.EnsureSuccessStatusCode();
            await test.ReleaseAsync("empty-prepared", "admitted");
            using var denied = await read;
            Assert.Equal(HttpStatusCode.ServiceUnavailable, denied.StatusCode);
            await test.WaitAsync("empty-prepared", "checkpointed");
        });

    [Theory]
    [InlineData(DriverLifecycleKind.WithdrawPersonal)]
    [InlineData(DriverLifecycleKind.DeleteUser)]
    public Task LifecycleLossBeforeFirstByteSuppressesHistoryAndCheckpointsActualWriter(DriverLifecycleKind kind) =>
        LifecycleTopology.RunAsync(postgres, $"percentile-history-unsent-{kind}", ["AUTH-03", "HTTP-01", "COPY-03"], async test =>
        {
            await PrepareAsync(test);
            await test.HoldAsync("unsent-history", "admitted");
            var read = test.Publisher.Client.GetAsync("/percentile-history/unsent-history", test.CancellationToken);
            await test.WaitAsync("unsent-history", "admitted");
            var operation = Guid.NewGuid();
            Assert.False((await test.TransitionAsync(operation, kind)).Completed);
            await test.ReleaseAsync("unsent-history", "admitted");
            using var denied = await read;
            Assert.Equal(HttpStatusCode.ServiceUnavailable, denied.StatusCode);
            Assert.DoesNotContain("percentileRank", await denied.Content.ReadAsStringAsync(test.CancellationToken));
            await test.WaitAsync("unsent-history", "checkpointed");
            Assert.True((await test.TransitionAsync(operation, kind)).Completed);
            await test.RestartBothAsync();
            using var restarted = await test.Publisher.Client.GetAsync("/percentile-history/restarted", test.CancellationToken);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, restarted.StatusCode);
        });

    [Theory]
    [InlineData(DriverLifecycleKind.WithdrawPersonal)]
    [InlineData(DriverLifecycleKind.DeleteUser)]
    public Task WriterDrainIncludesHistoryTransportAndExpiryCannotReplaceTerminalCheckpoint(DriverLifecycleKind kind) =>
        LifecycleTopology.RunAsync(postgres, $"percentile-history-writing-{kind}", ["AUTH-03", "HTTP-01"], async test =>
        {
            await PrepareAsync(test);
            await test.HoldAsync("writing-history", "first-written");
            var read = test.Publisher.Client.GetAsync("/percentile-history/writing-history", HttpCompletionOption.ResponseHeadersRead, test.CancellationToken);
            await test.WaitAsync("writing-history", "first-written");
            var operation = Guid.NewGuid();
            var pending = await test.TransitionAsync(operation, kind);
            Assert.False(pending.Completed);
            Assert.Equal(1, pending.PendingWriters);
            await test.ExpireLeasesAsync();
            Assert.False((await test.TransitionAsync(operation, kind)).Completed);
            await test.ReleaseAsync("writing-history", "first-written");
            using var response = await read;
            response.EnsureSuccessStatusCode();
            Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync(test.CancellationToken));
            await test.WaitAsync("writing-history", "checkpointed");
            Assert.True((await test.TransitionAsync(operation, kind)).Completed);
        });

    private static async Task PrepareAsync(LifecycleTopology test)
    {
        await using var db = test.OpenPrimary();
        db.Cars.Add(new() { Id = 99, Name = "Synthetic History Car", NameAbbreviated = "SH" });
        db.Tracks.Add(new() { Id = 42, Name = "Synthetic History Track", ConfigName = "Exact layout" });
        db.Series.Add(new() { Id = 1, Name = "Synthetic History Series" });
        db.Seasons.Add(new() { Id = 1, SeriesId = 1, Year = 2026, Quarter = 4, Active = true });
        db.CarClasses.Add(new() { Id = 1, Name = "Synthetic Class", ShortName = "SC" });
        var week = Guid.NewGuid();
        db.Weeks.Add(new() { Id = week, SeasonId = 1, TrackId = 42, StartDate = new(1970, 1, 1),
            EndTime = DateTimeOffset.UtcNow.AddDays(1) });
        await db.SaveChangesAsync(test.CancellationToken);
        var copies = new EvidenceCopyLifecycle(db, TimeProvider.System);
        var purpose = await copies.OpenSyntheticPurposeAsync(EvidencePurposeKind.IndependentOfficial, 1, ct: test.CancellationToken);
        var receipt = await copies.CaptureAsync(purpose, EvidenceCopyKind.OfficialField, "synthetic-history-field", ct: test.CancellationToken);
        await copies.CommitAsync(receipt, new OfficialFieldBatch(new() { Id = 1, SeasonId = 1, TrackId = 42,
            WeekId = week, OfficialSession = true }, new double[] { 105, 100, 110, 115, 120 }.Select((lap, index) => new SubsessionResult
            { SubsessionId = 1, CustId = index == 0 ? SyntheticLifecycleActors.Scope.CustomerId : 2000 + index,
                CarId = 99, CarClassId = 1, BestLapSeconds = lap }).ToArray()), test.CancellationToken);
        await test.GrantAsync();
    }

    private static async Task<HttpResponseMessage> UploadAsync(LifecycleTopology test, float seconds)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(FakeIbtBuilder.Build(laps: 2, lapTime: seconds,
            customerId: SyntheticLifecycleActors.Scope.CustomerId)), "file", "synthetic-history.ibt");
        return await test.Publisher.Client.PostAsync("/upload", content, test.CancellationToken);
    }
}
