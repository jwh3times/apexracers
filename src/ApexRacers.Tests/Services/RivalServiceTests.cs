using ApexRacers.Api.Services;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Tests.Helpers;
using Aydsko.iRacingData;
using Aydsko.iRacingData.Common;
using Aydsko.iRacingData.Lookups;
using NSubstitute;
using Xunit;

namespace ApexRacers.Tests.Services;

public class RivalServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class StubServiceProvider(object? service) : IServiceProvider
    {
        public object? GetService(Type serviceType) => service;
    }

    private static RivalService Build(AppDbContext db, IDataClient? client = null) =>
        new(db, new CachedIRacingClient(db, new StubServiceProvider(client)));

    private static void SeedResult(AppDbContext db, int subsessionId, long custId, string name,
        int finish = 1, double bestLap = -1)
    {
        db.SubsessionResults.Add(new SubsessionResult
        {
            SubsessionId = subsessionId,
            CustId = custId,
            DisplayName = name,
            FinishPosition = finish,
            BestLapSeconds = bestLap,
        });
    }

    // ── Add / List / Remove ──────────────────────────────────────────────────

    [Fact]
    public async Task AddAsync_NewRival_Persists()
    {
        await using var db = DbContextFactory.Create();
        var service = Build(db);
        var userId = Guid.NewGuid();

        var dto = await service.AddAsync(userId, 12345, "Max Power", Ct);

        Assert.Equal(12345, dto.CustId);
        Assert.Equal("Max Power", dto.DisplayName);
        Assert.Single(db.Rivals.Where(r => r.UserId == userId));
    }

    [Fact]
    public async Task AddAsync_Duplicate_IsIdempotent_KeepsOriginal()
    {
        await using var db = DbContextFactory.Create();
        var service = Build(db);
        var userId = Guid.NewGuid();

        var first = await service.AddAsync(userId, 12345, "Max Power", Ct);
        var second = await service.AddAsync(userId, 12345, "Different Name", Ct);

        Assert.Single(db.Rivals.Where(r => r.UserId == userId));
        Assert.Equal(first.CreatedAt, second.CreatedAt);
        Assert.Equal("Max Power", second.DisplayName); // original kept
    }

    [Fact]
    public async Task AddAsync_BlankDisplayName_DefaultsToDriverCustId()
    {
        await using var db = DbContextFactory.Create();
        var service = Build(db);

        var dto = await service.AddAsync(Guid.NewGuid(), 555, "   ", Ct);

        Assert.Equal("Driver 555", dto.DisplayName);
    }

    [Fact]
    public async Task AddAsync_InvalidCustId_Throws()
    {
        await using var db = DbContextFactory.Create();
        var service = Build(db);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.AddAsync(Guid.NewGuid(), 0, "Nobody", Ct));
    }

    [Fact]
    public async Task ListAsync_ReturnsOnlyCallersRivals_NewestFirst()
    {
        await using var db = DbContextFactory.Create();
        var me = Guid.NewGuid();
        var other = Guid.NewGuid();
        db.Rivals.AddRange(
            new Rival { Id = Guid.NewGuid(), UserId = me, RivalCustId = 1, DisplayName = "Older", CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) },
            new Rival { Id = Guid.NewGuid(), UserId = me, RivalCustId = 2, DisplayName = "Newer", CreatedAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero) },
            new Rival { Id = Guid.NewGuid(), UserId = other, RivalCustId = 3, DisplayName = "Theirs", CreatedAt = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero) });
        await db.SaveChangesAsync(Ct);
        var service = Build(db);

        var result = await service.ListAsync(me, Ct);

        Assert.Equal(["Newer", "Older"], result.Select(r => r.DisplayName).ToArray());
    }

    [Fact]
    public async Task RemoveAsync_DeletesOnlyThatUsersRival()
    {
        await using var db = DbContextFactory.Create();
        var me = Guid.NewGuid();
        var other = Guid.NewGuid();
        db.Rivals.AddRange(
            new Rival { Id = Guid.NewGuid(), UserId = me, RivalCustId = 7, DisplayName = "Mine", CreatedAt = DateTimeOffset.UtcNow },
            new Rival { Id = Guid.NewGuid(), UserId = other, RivalCustId = 7, DisplayName = "Theirs", CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(Ct);
        var service = Build(db);

        await service.RemoveAsync(me, 7, Ct);

        Assert.Empty(db.Rivals.Where(r => r.UserId == me));
        Assert.Single(db.Rivals.Where(r => r.UserId == other));
    }

    [Fact]
    public async Task RemoveAsync_NonExistent_IsNoOp()
    {
        await using var db = DbContextFactory.Create();
        var service = Build(db);

        await service.RemoveAsync(Guid.NewGuid(), 999, Ct); // does not throw
    }

    // ── SearchDrivers ────────────────────────────────────────────────────────

    private static IDataClient ClientReturningSearch(params (int custId, string name)[] hits)
    {
        var client = Substitute.For<IDataClient>();
        var results = hits.Select(h => new DriverSearchResult { CustomerId = h.custId, DisplayName = h.name }).ToArray();
        client.SearchDriversAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new DataResponse<DriverSearchResult[]> { Data = results });
        return client;
    }

    [Fact]
    public async Task SearchDriversAsync_MapsHits()
    {
        await using var db = DbContextFactory.Create();
        var client = ClientReturningSearch((691062, "Jerry Holland"), (12345, "Max Power"));
        var service = Build(db, client);

        var result = await service.SearchDriversAsync("holland", Ct);

        Assert.Equal(2, result.Count);
        Assert.Equal(691062, result[0].CustId);
        Assert.Equal("Jerry Holland", result[0].DisplayName);
    }

    [Fact]
    public async Task SearchDriversAsync_ShortTerm_ReturnsEmpty_WithoutCallingApi()
    {
        await using var db = DbContextFactory.Create();
        var client = ClientReturningSearch((1, "X"));
        var service = Build(db, client);

        var result = await service.SearchDriversAsync("a", Ct);

        Assert.Empty(result);
        await client.DidNotReceive().SearchDriversAsync(
            Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchDriversAsync_SameTermTwice_CachesAndFetchesOnce()
    {
        await using var db = DbContextFactory.Create();
        var client = ClientReturningSearch((691062, "Jerry Holland"));
        var service = Build(db, client);

        await service.SearchDriversAsync("holland", Ct);
        await service.SearchDriversAsync("Holland", Ct); // same after normalize

        await client.Received(1).SearchDriversAsync(
            Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    // ── Suggestions ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SuggestionsAsync_RanksCoRacersBySharedCount_ExcludesSelfAndFollowed()
    {
        await using var db = DbContextFactory.Create();
        const long me = 100;
        // I raced subs 1,2,3. RivalA in 1,2 (2 shared). RivalB in 1 (1 shared). I'm in all.
        SeedResult(db, 1, me, "Me");
        SeedResult(db, 2, me, "Me");
        SeedResult(db, 3, me, "Me");
        SeedResult(db, 1, 200, "Rival A");
        SeedResult(db, 2, 200, "Rival A");
        SeedResult(db, 1, 300, "Rival B");
        SeedResult(db, 1, 400, "Followed"); // already a rival → excluded
        var userId = Guid.NewGuid();
        db.Rivals.Add(new Rival { Id = Guid.NewGuid(), UserId = userId, RivalCustId = 400, DisplayName = "Followed", CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(Ct);
        var service = Build(db);

        var result = await service.SuggestionsAsync(userId, me, Ct);

        Assert.Equal(2, result.Count);
        Assert.Equal(200, result[0].CustId);   // most shared races first
        Assert.Equal(2, result[0].SharedRaces);
        Assert.Equal("Rival A", result[0].DisplayName);
        Assert.Equal(300, result[1].CustId);
        Assert.Equal(1, result[1].SharedRaces);
        Assert.DoesNotContain(result, s => s.CustId == me || s.CustId == 400);
    }

    [Fact]
    public async Task SuggestionsAsync_NoSharedRaces_ReturnsEmpty()
    {
        await using var db = DbContextFactory.Create();
        SeedResult(db, 1, 100, "Me");
        await db.SaveChangesAsync(Ct);
        var service = Build(db);

        var result = await service.SuggestionsAsync(Guid.NewGuid(), 100, Ct);

        Assert.Empty(result);
    }
}
