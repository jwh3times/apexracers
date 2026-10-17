using System.Collections;
using System.Data.Common;
using System.Text.Json;
using ApexRacers.Api.Services;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Core.PercentileHistory;
using ApexRacers.Data;
using ApexRacers.Seeder.Demo;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace ApexRacers.Tests.Services;

[Collection(PostgreSqlCollection.Name)]
public sealed class MappedSyntheticPreparationTests(PostgreSqlFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RepeatedMappedUpsertsDoNotDiscoverOrMaterializeUnrelatedFields()
    {
        var counts = new FieldReads();
        await using var db = await CreateAsync(counts);
        await FieldAsync(db);
        var fieldBefore = JsonSerializer.Serialize(await db.EvidenceCopyMarkers.AsNoTracking()
            .Where(m => m.Kind == EvidenceCopyKind.OfficialField).OrderBy(m => m.Id).ToListAsync(Ct));
        counts.Enabled = true;
        await UpsertAsync(db);
        var firstMapped = await db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(m => m.Kind == EvidenceCopyKind.MappedCache, Ct);
        for (var repeat = 0; repeat < 2; repeat++) await UpsertAsync(db);
        var originalMapped = await db.EvidenceCopyMarkers.AsNoTracking().SingleAsync(m => m.Id == firstMapped.Id, Ct);
        Assert.Equal(firstMapped.OriginalAcquiredAt, originalMapped.OriginalAcquiredAt);
        Assert.Equal(firstMapped.ExpiresAt, originalMapped.ExpiresAt);
        Assert.Equal(fieldBefore, JsonSerializer.Serialize(await db.EvidenceCopyMarkers.AsNoTracking()
            .Where(m => m.Kind == EvidenceCopyKind.OfficialField).OrderBy(m => m.Id).ToListAsync(Ct)));
        Assert.Equal((0, 0, 0, 0), (counts.LegacyQueries, counts.ClassifiedQueries, counts.LegacyRows, counts.ClassifiedRows));
        var profile = await new CachedIRacingClient(db, null).GetOrFetchAsync<ProfileSnapshot>(
            IRacingCacheKeys.Profile(DemoData.DriverCustId), _ => throw new InvalidOperationException("Demo must remain offline."), Ct);
        Assert.Equal("Demo Driver", profile.DisplayName);
        Assert.Single(await db.ExternalDataCaches.ToListAsync(Ct));
        Assert.Equal(5, await db.SubsessionResults.CountAsync(Ct));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImplicitPendingDerivativeWithAutomaticDetectionOffRetainsPreparedSourcesAndInternalMetadata(bool classified)
    {
        var counts = new FieldReads();
        await using var db = await CreateAsync(counts);
        var week = await FieldAsync(db);
        var user = await UserAsync(db);
        var originalWriter = await SyntheticEvidenceWriter.OpenAsync(db, Ct);
        var derivative = Derivative(classified, user, week);
        db.Add(derivative);
        await originalWriter.SaveChangesAsync(Ct);
        var oldCopy = derivative.EvidenceCopyId;
        var purposeBefore = await db.EvidencePurposes.AsNoTracking().SingleAsync(Ct);
        SetRank(derivative, 70);
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        counts.Enabled = true;
        await UpsertAsync(db);
        Assert.False(db.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal((2, 2, 2, 2), (counts.LegacyQueries, counts.ClassifiedQueries, counts.LegacyRows, counts.ClassifiedRows));
        var retained = classified
            ? (IManagedEvidence)await db.ClassifiedCarPercentileResults.AsNoTracking().SingleAsync(Ct)
            : await db.CarPercentileResults.AsNoTracking().SingleAsync(Ct);
        Assert.NotEqual(oldCopy, retained.EvidenceCopyId);
        Assert.Equal(70, Rank(retained));
        Assert.Equal(purposeBefore.EvidenceVersion + 1,
            (await db.EvidencePurposes.AsNoTracking().SingleAsync(Ct)).EvidenceVersion);
        Assert.Single(await db.ExternalDataCaches.ToListAsync(Ct));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task UnpreparedMappedSessionRefusesLateAddedOrImplicitlyModifiedDerivativeAndRetry(bool classified, bool existing)
    {
        var counts = new FieldReads();
        await using var db = await CreateAsync(counts);
        var week = await FieldAsync(db);
        var user = await UserAsync(db);
        var derivative = Derivative(classified, user, week);
        if (existing)
        {
            var full = await SyntheticEvidenceWriter.OpenAsync(db, Ct);
            db.Add(derivative);
            await full.SaveChangesAsync(Ct);
        }
        counts.Enabled = true;
        var mapped = await SyntheticEvidenceWriter.OpenMappedAsync(db, Ct);
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        if (existing) SetRank(derivative, 70);
        else db.Add(derivative);
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => mapped.SaveChangesAsync(Ct));
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => mapped.SaveChangesAsync(Ct));
        Assert.False(db.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal((0, 0, 0, 0), (counts.LegacyQueries, counts.ClassifiedQueries, counts.LegacyRows, counts.ClassifiedRows));
        var legacy = await db.CarPercentileResults.IgnoreQueryFilters().AsNoTracking().ToListAsync(Ct);
        var current = await db.ClassifiedCarPercentileResults.IgnoreQueryFilters().AsNoTracking().ToListAsync(Ct);
        Assert.Equal(existing ? 1 : 0, legacy.Count + current.Count);
        if (existing) Assert.Equal(90, Rank(classified ? current.Single() : legacy.Single()));
        Assert.Empty(await db.ExternalDataCaches.ToListAsync(Ct));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PendingAddedDerivativeRetainsFullPreparationAlongsideActualMappedUpsert(bool classified, bool automatic)
    {
        var counts = new FieldReads();
        await using var db = await CreateAsync(counts);
        var week = await FieldAsync(db);
        var user = await UserAsync(db);
        var derivative = Derivative(classified, user, week);
        db.Add(derivative);
        db.ChangeTracker.AutoDetectChangesEnabled = automatic;
        counts.Enabled = true;
        await UpsertAsync(db);
        Assert.Equal(automatic, db.ChangeTracker.AutoDetectChangesEnabled);
        Assert.Equal((2, 2, 2, 2), (counts.LegacyQueries, counts.ClassifiedQueries, counts.LegacyRows, counts.ClassifiedRows));
        Assert.NotNull(derivative.EvidenceCopyId);
        var sources = await db.EvidenceCopyDependencies.Where(d => d.CopyId == derivative.EvidenceCopyId).ToListAsync(Ct);
        Assert.Single(sources);
        Assert.Single(await db.ExternalDataCaches.ToListAsync(Ct));
        Assert.Equal(5, await db.SubsessionResults.CountAsync(Ct));
    }

    [Theory]
    [InlineData(false, 3)]
    [InlineData(true, 1)]
    public async Task FullPreparationKeepsLegacyAndClassifiedFieldEligibilityDistinct(bool classified, int expectedSources)
    {
        await using var db = await CreateAsync();
        var week = await FieldAsync(db);
        var user = await UserAsync(db);
        db.Tracks.Add(new() { Id = 2, Name = "Other synthetic layout" });
        await db.SaveChangesAsync(Ct);
        await IndependentFieldAsync(db, week, 2, 2, 80);
        await IndependentFieldAsync(db, week, 3, 1, 0);
        var writer = await SyntheticEvidenceWriter.OpenAsync(db, Ct);
        var derivative = Derivative(classified, user, week);
        db.Add(derivative);
        await writer.SaveChangesAsync(Ct);
        var sources = await db.EvidenceCopyDependencies.Where(d => d.CopyId == derivative.EvidenceCopyId)
            .Select(d => d.SourceCopyId).ToListAsync(Ct);
        Assert.Equal(expectedSources, sources.Count);
        var eligible = await db.Subsessions.Where(r => r.Id == 1).Select(r => r.EvidenceCopyId).SingleAsync(Ct);
        Assert.Contains(eligible!.Value, sources);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PreparedCalculationRefusesChangedCompleteSourceSetAndCannotRetry(bool classified, bool remove)
    {
        await using var db = await CreateAsync();
        var week = await FieldAsync(db);
        var user = await UserAsync(db);
        await IndependentFieldAsync(db, week, 2, 1, 80);
        var writer = await SyntheticEvidenceWriter.OpenAsync(db, Ct);
        var prepared = await db.EvidencePurposes.AsNoTracking()
            .SingleAsync(p => p.Kind == EvidencePurposeKind.SyntheticPreview, Ct);
        // This independent source purpose does not advance the writer's own synthetic purpose.
        if (remove)
            await db.Database.ExecuteSqlRawAsync("DELETE FROM iracing.\"RaceEvidenceSubsessions\" WHERE \"Id\" = 2", Ct);
        else await IndependentFieldAsync(db, week, 3, 1, 85);
        var currentPurpose = await db.EvidencePurposes.AsNoTracking().SingleAsync(p => p.Id == prepared.Id, Ct);
        Assert.Equal(prepared.Generation, currentPurpose.Generation);
        Assert.Equal(prepared.EvidenceVersion, currentPurpose.EvidenceVersion);
        Assert.Equal(prepared.OriginalEndedAt, currentPurpose.OriginalEndedAt);
        db.Add(Derivative(classified, user, week));
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => writer.SaveChangesAsync(Ct));
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => writer.SaveChangesAsync(Ct));
        Assert.Empty(await db.CarPercentileResults.IgnoreQueryFilters().ToListAsync(Ct));
        Assert.Empty(await db.ClassifiedCarPercentileResults.IgnoreQueryFilters().ToListAsync(Ct));
    }

    [Fact]
    public async Task MappedPreparationRetainsPurposeVersionFenceAndFailedSessionRefusal()
    {
        await using var db = await CreateAsync();
        await FieldAsync(db);
        await UpsertAsync(db);
        var writer = await SyntheticEvidenceWriter.OpenMappedAsync(db, Ct);
        await UpsertAsync(db); // A separate legitimate prepared write advances the same purpose.
        var cache = await db.ExternalDataCaches.SingleAsync(Ct);
        cache.Payload = "{\"displayName\":\"must not commit\"}";
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => writer.SaveChangesAsync(Ct));
        await Assert.ThrowsAsync<EvidenceCopyUnavailableException>(() => writer.SaveChangesAsync(Ct));
        var retained = await new CachedIRacingClient(db, null).GetOrFetchAsync<ProfileSnapshot>(
            IRacingCacheKeys.Profile(DemoData.DriverCustId), _ => throw new InvalidOperationException("Demo must remain offline."), Ct);
        Assert.Equal("Demo Driver", retained.DisplayName);
    }

    private static async Task IndependentFieldAsync(AppDbContext db, Guid week, int id, int track, double lap)
    {
        var lifecycle = new EvidenceCopyLifecycle(db, TimeProvider.System);
        var purpose = await lifecycle.OpenSyntheticPurposeAsync(EvidencePurposeKind.IndependentOfficial, 1, ct: Ct);
        var receipt = await lifecycle.CaptureAsync(purpose, EvidenceCopyKind.OfficialField, $"field:{id}", ct: Ct);
        await lifecycle.CommitAsync(receipt, new OfficialFieldBatch(
            new() { Id = id, SeasonId = 1, TrackId = track, WeekId = week, OfficialSession = true, StartTime = DateTimeOffset.UtcNow },
            [new() { SubsessionId = id, CustId = 300001, CarId = 1, CarClassId = 1, BestLapSeconds = lap }]), Ct);
    }

    private static async Task<Guid> UserAsync(AppDbContext db)
    {
        var user = Guid.NewGuid();
        db.Users.Add(new() { Id = user, DisplayName = "Synthetic User" });
        await db.SaveChangesAsync(Ct);
        return user;
    }

    private static IManagedEvidence Derivative(bool classified, Guid user, Guid week) => classified
        ? new ClassifiedCarPercentileResult
        {
            Id = Guid.NewGuid(),
            Provenance = DataProvenance.Demo,
            UserId = user,
            CustomerId = checked((int)DemoData.DriverCustId),
            OwnerKind = PercentileOwnerKind.DemoSubject,
            SelectionPolicyVersion = 1,
            SelectionKey = "v1:0:0",
            CarId = 1,
            SeriesId = 1,
            WeekId = week,
            PercentileRank = 90,
            TopSharePercent = 20,
            SampleSize = 5,
            ComputedAt = DateTimeOffset.UtcNow,
            PersonalBestLapSeconds = 90,
            PersonalBestLapEvidence = LapEvidence.RaceLap
        }
        : new CarPercentileResult
        {
            Id = Guid.NewGuid(),
            Provenance = DataProvenance.Demo,
            UserId = user,
            CarId = 1,
            SeriesId = 1,
            WeekId = week,
            PercentileRank = 90,
            TopSharePercent = 20,
            SampleSize = 5,
            ComputedAt = DateTimeOffset.UtcNow
        };

    private static double Rank(IManagedEvidence derivative) => derivative switch
    {
        CarPercentileResult legacy => legacy.PercentileRank,
        ClassifiedCarPercentileResult row => row.PercentileRank,
        _ => throw new InvalidOperationException()
    };

    private static void SetRank(IManagedEvidence derivative, double rank)
    {
        if (derivative is CarPercentileResult legacy) legacy.PercentileRank = rank;
        else ((ClassifiedCarPercentileResult)derivative).PercentileRank = rank;
    }

    private static Task UpsertAsync(AppDbContext db) => DemoCache.UpsertAsync(db,
        IRacingCacheKeys.Profile(DemoData.DriverCustId).Key, DemoMemberData.BuildProfile(DemoData.DriverCustId), Ct);

    private async Task<AppDbContext> CreateAsync(params IInterceptor[] interceptors)
    {
        var options = await postgres.CreateOptionsAsync(Ct);
        await using (var setup = new AppDbContext(options))
        { await setup.Database.EnsureDeletedAsync(Ct); await setup.Database.MigrateAsync(Ct); }
        return new(postgres.WithInterceptors(options, interceptors), new IRacingDataScope(DataProvenance.Demo));
    }

    private static async Task<Guid> FieldAsync(AppDbContext db)
    {
        db.Series.Add(new() { Id = 1, Name = "Synthetic Series" });
        db.Seasons.Add(new() { Id = 1, SeriesId = 1, Year = 2026, Quarter = 2, Active = true });
        db.Cars.Add(new() { Id = 1, Name = "Synthetic Car", NameAbbreviated = "SC" });
        db.CarClasses.Add(new() { Id = 1, Name = "Synthetic Class", ShortName = "SC" });
        db.Tracks.Add(new() { Id = 1, Name = "Synthetic Track" });
        var week = Guid.NewGuid();
        db.Weeks.Add(new() { Id = week, SeasonId = 1, TrackId = 1, StartDate = new(2026, 6, 1) });
        var writer = await SyntheticEvidenceWriter.OpenAsync(db, Ct);
        await writer.SaveChangesAsync(Ct);
        db.Subsessions.Add(new() { Id = 1, SeasonId = 1, TrackId = 1, WeekId = week, StartTime = DateTimeOffset.UtcNow });
        db.SubsessionResults.AddRange(Enumerable.Range(0, 5).Select(i => new SubsessionResult
        { SubsessionId = 1, CustId = DemoData.DriverCustId + i, CarId = 1, CarClassId = 1, BestLapSeconds = 90 + i }));
        await writer.SaveChangesAsync(Ct);
        return week;
    }

    private sealed class FieldReads : DbCommandInterceptor
    {
        public bool Enabled { get; set; }
        public int LegacyQueries { get; private set; }
        public int ClassifiedQueries { get; private set; }
        public int LegacyRows { get; private set; }
        public int ClassifiedRows { get; private set; }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data,
            DbDataReader result, CancellationToken ct = default)
        {
            // Exact actual Field projection; a dependency CopyId SELECT is a different operation.
            if (!Enabled || !command.CommandText.StartsWith("SELECT DISTINCT r1.\"WeekId\", r.\"CarId\", r.\"EvidenceCopyId\" AS \"CopyId\""))
                return ValueTask.FromResult(result);
            var classified = command.CommandText.Contains("BestLapSeconds");
            if (classified) ClassifiedQueries++; else LegacyQueries++;
            return ValueTask.FromResult<DbDataReader>(new CountingReader(result,
                () => { if (classified) ClassifiedRows++; else LegacyRows++; }));
        }
    }

    private sealed class CountingReader(DbDataReader inner, Action read) : DbDataReader
    {
        public override bool Read() { var success = inner.Read(); if (success) read(); return success; }
        public override async Task<bool> ReadAsync(CancellationToken ct) { var success = await inner.ReadAsync(ct); if (success) read(); return success; }
        public override bool NextResult() => inner.NextResult();
        public override Task<bool> NextResultAsync(CancellationToken ct) => inner.NextResultAsync(ct);
        public override object this[int ordinal] => inner[ordinal];
        public override object this[string name] => inner[name];
        public override int Depth => inner.Depth;
        public override int FieldCount => inner.FieldCount;
        public override bool HasRows => inner.HasRows;
        public override bool IsClosed => inner.IsClosed;
        public override int RecordsAffected => inner.RecordsAffected;
        public override bool GetBoolean(int ordinal) => inner.GetBoolean(ordinal);
        public override byte GetByte(int ordinal) => inner.GetByte(ordinal);
        public override long GetBytes(int ordinal, long offset, byte[]? buffer, int bufferOffset, int length) => inner.GetBytes(ordinal, offset, buffer, bufferOffset, length);
        public override char GetChar(int ordinal) => inner.GetChar(ordinal);
        public override long GetChars(int ordinal, long offset, char[]? buffer, int bufferOffset, int length) => inner.GetChars(ordinal, offset, buffer, bufferOffset, length);
        public override string GetDataTypeName(int ordinal) => inner.GetDataTypeName(ordinal);
        public override DateTime GetDateTime(int ordinal) => inner.GetDateTime(ordinal);
        public override decimal GetDecimal(int ordinal) => inner.GetDecimal(ordinal);
        public override double GetDouble(int ordinal) => inner.GetDouble(ordinal);
        public override Type GetFieldType(int ordinal) => inner.GetFieldType(ordinal);
        public override float GetFloat(int ordinal) => inner.GetFloat(ordinal);
        public override Guid GetGuid(int ordinal) => inner.GetGuid(ordinal);
        public override short GetInt16(int ordinal) => inner.GetInt16(ordinal);
        public override int GetInt32(int ordinal) => inner.GetInt32(ordinal);
        public override long GetInt64(int ordinal) => inner.GetInt64(ordinal);
        public override string GetName(int ordinal) => inner.GetName(ordinal);
        public override int GetOrdinal(string name) => inner.GetOrdinal(name);
        public override string GetString(int ordinal) => inner.GetString(ordinal);
        public override object GetValue(int ordinal) => inner.GetValue(ordinal);
        public override int GetValues(object[] values) => inner.GetValues(values);
        public override bool IsDBNull(int ordinal) => inner.IsDBNull(ordinal);
        public override T GetFieldValue<T>(int ordinal) => inner.GetFieldValue<T>(ordinal);
        public override Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken ct) => inner.GetFieldValueAsync<T>(ordinal, ct);
        public override IEnumerator GetEnumerator() => ((IEnumerable)inner).GetEnumerator();
        public override void Close() => inner.Close();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        public override ValueTask DisposeAsync() => inner.DisposeAsync();
    }

}
