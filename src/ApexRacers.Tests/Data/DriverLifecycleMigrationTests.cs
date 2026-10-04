using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using ApexRacers.Data.Migrations;
using ApexRacers.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace ApexRacers.Tests.Data;

[Collection(PostgreSqlCollection.Name)]
public sealed class DriverLifecycleMigrationTests(PostgreSqlFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MigrationPreservesLegacyClaimWithoutInventingProofAndFencesOldWriters()
    {
        await using var db = await postgres.CreateDbContextAsync(Ct);
        await db.Database.EnsureDeletedAsync(Ct);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261003030330_IsolateDemoProvenance", Ct);
        var userId = Guid.NewGuid();
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO identity."Users"
                ("Id", "DisplayName", "IRacingCustomerId", "EmailConfirmed", "PhoneNumberConfirmed",
                 "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
            VALUES ({0}, 'Synthetic legacy claimant', 42001, false, false, false, false, 0)
            """, [userId], Ct);

        await migrator.MigrateAsync(cancellationToken: Ct);
        Assert.Equal(42001L, (await db.Users.SingleAsync(Ct)).IRacingCustomerId);
        Assert.Empty(await db.DriverAuthorizationGrants.ToListAsync(Ct));
        Assert.Empty(await db.DriverProofReceipts.ToListAsync(Ct));
        Assert.Empty(await db.DriverLifecycleOperations.ToListAsync(Ct));
        Assert.Empty(await db.DriverCopyCleanups.ToListAsync(Ct));
        var oldWriter = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE identity.\"Users\" SET \"IRacingCustomerId\" = 42002", Ct));
        Assert.Equal(PostgresErrorCodes.UndefinedColumn, oldWriter.SqlState);
        Assert.Throws<NotSupportedException>(() => new DriverLifecycleAdmissionSpine().DownOperations);
    }

    [Fact]
    public async Task ProofReceiptCannotAuthorizeAnotherUserDriverOrNamespaceAndActiveBindingsConflict()
    {
        await using var db = await MigratedAsync();
        var firstUser = NewUser();
        var otherUser = NewUser();
        db.Users.AddRange(firstUser, otherUser);
        await db.SaveChangesAsync(Ct);
        var realReceipt = Receipt(firstUser.Id, 42001, DataProvenance.Real);
        var otherReceipt = Receipt(otherUser.Id, 42001, DataProvenance.Real);
        var demoReceipt = Receipt(firstUser.Id, 42001, DataProvenance.Demo);
        db.DriverProofReceipts.AddRange(realReceipt, otherReceipt, demoReceipt);
        await db.SaveChangesAsync(Ct);

        foreach (var scope in new[]
        {
            new DriverScope(otherUser.Id, 42001, DataProvenance.Real),
            new DriverScope(firstUser.Id, 42002, DataProvenance.Real),
            new DriverScope(firstUser.Id, 42001, DataProvenance.Demo),
        })
        {
            db.DriverAuthorizationGrants.Add(Grant(realReceipt.Id, scope.UserId, scope.CustomerId, scope.Provenance));
            await AssertSqlStateAsync(db, PostgresErrorCodes.ForeignKeyViolation);
        }

        db.DriverAuthorizationGrants.AddRange(
            Grant(realReceipt.Id, firstUser.Id, 42001, DataProvenance.Real),
            Grant(demoReceipt.Id, firstUser.Id, 42001, DataProvenance.Demo));
        await db.SaveChangesAsync(Ct);
        db.DriverAuthorizationGrants.Add(Grant(otherReceipt.Id, otherUser.Id, 42001, DataProvenance.Real));
        await AssertSqlStateAsync(db, PostgresErrorCodes.UniqueViolation);

        var secondDriverReceipt = Receipt(firstUser.Id, 42002, DataProvenance.Real);
        db.DriverProofReceipts.Add(secondDriverReceipt);
        await db.SaveChangesAsync(Ct);
        db.DriverAuthorizationGrants.Add(Grant(secondDriverReceipt.Id, firstUser.Id, 42002, DataProvenance.Real));
        await AssertSqlStateAsync(db, PostgresErrorCodes.UniqueViolation);
        Assert.Equal(2, await db.DriverAuthorizationGrants.CountAsync(Ct));
    }

    [Fact]
    public async Task CleanupCannotChangeOriginalAssociationAndLeaseExpiryDoesNotRemovePendingWriters()
    {
        await using var db = await MigratedAsync();
        var user = NewUser();
        db.Users.Add(user);
        var receipt = Receipt(user.Id, 42001, DataProvenance.Demo);
        var otherReceipt = Receipt(user.Id, 42002, DataProvenance.Demo);
        db.DriverProofReceipts.AddRange(receipt, otherReceipt);
        var grant = Grant(receipt.Id, user.Id, 42001, DataProvenance.Demo);
        var otherGrant = Grant(otherReceipt.Id, user.Id, 42002, DataProvenance.Demo);
        otherGrant.BindingActive = false;
        db.DriverAuthorizationGrants.AddRange(grant, otherGrant);
        var loss = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var operation = new DriverLifecycleOperation
        {
            Id = Guid.NewGuid(), GrantId = grant.Id, Kind = DriverLifecycleKind.Unlink,
            OriginalLossAt = loss, AppliedRevision = 2, PrimaryAppliedAt = loss.AddDays(1),
        };
        db.DriverLifecycleOperations.Add(operation);
        db.DriverPublicationAdmissions.Add(new DriverPublicationAdmission
        {
            Id = Guid.NewGuid(), GrantId = grant.Id, Revision = 1, Purpose = DriverConsentScope.Personal,
            Incarnation = Guid.NewGuid(), AdmittedAt = loss.AddMinutes(-5), LeaseUntil = loss,
        });
        await db.SaveChangesAsync(Ct);

        db.DriverCopyCleanups.Add(new DriverCopyCleanup
        {
            Id = Guid.NewGuid(), OperationId = operation.Id, GrantId = otherGrant.Id,
            Purpose = DriverConsentScope.Personal, OriginalLossAt = loss, DueAt = loss.AddDays(97),
        });
        await AssertSqlStateAsync(db, PostgresErrorCodes.ForeignKeyViolation);

        db.DriverCopyCleanups.Add(new DriverCopyCleanup
        {
            Id = Guid.NewGuid(), OperationId = operation.Id, GrantId = grant.Id,
            Purpose = DriverConsentScope.Personal, ThroughRevision = 0,
            OriginalLossAt = loss, DueAt = loss.AddDays(97),
        });
        await AssertSqlStateAsync(db, PostgresErrorCodes.CheckViolation);

        db.DriverCopyCleanups.Add(new DriverCopyCleanup
        {
            Id = Guid.NewGuid(), OperationId = operation.Id, GrantId = grant.Id,
            Purpose = DriverConsentScope.Personal, OriginalLossAt = loss.AddDays(1), DueAt = loss.AddDays(98),
        });
        await AssertSqlStateAsync(db, PostgresErrorCodes.ForeignKeyViolation);

        db.DriverCopyCleanups.Add(new DriverCopyCleanup
        {
            Id = Guid.NewGuid(), OperationId = operation.Id, GrantId = grant.Id,
            Purpose = DriverConsentScope.Personal, OriginalLossAt = loss, DueAt = loss.AddDays(97),
        });
        await db.SaveChangesAsync(Ct);
        Assert.Equal(loss, (await db.DriverCopyCleanups.SingleAsync(Ct)).OriginalLossAt);
        Assert.Null((await db.DriverPublicationAdmissions.SingleAsync(Ct)).TerminalAt);
        Assert.Null((await db.DriverLifecycleOperations.SingleAsync(Ct)).CompletedAt);
        var accountRemoval = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "DELETE FROM identity.\"Users\" WHERE \"Id\" = {0}", [user.Id], Ct));
        Assert.Equal(PostgresErrorCodes.RestrictViolation, accountRemoval.SqlState);
        Assert.Single(await db.DriverLifecycleOperations.ToListAsync(Ct));
    }

    private async Task<AppDbContext> MigratedAsync()
    {
        var db = await postgres.CreateDbContextAsync(Ct);
        await db.Database.EnsureDeletedAsync(Ct);
        await db.Database.MigrateAsync(Ct);
        return db;
    }

    private static ApplicationUser NewUser() => new() { Id = Guid.NewGuid(), DisplayName = "Synthetic owner" };
    private static DriverProofReceipt Receipt(Guid userId, int customerId, DataProvenance provenance) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, CustomerId = customerId, Provenance = provenance,
        VerifiedAt = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), Authority = "synthetic-test",
    };
    private static DriverAuthorizationGrant Grant(Guid receiptId, Guid userId, int customerId, DataProvenance provenance) => new()
    {
        Id = Guid.NewGuid(), ProofReceiptId = receiptId, UserId = userId, CustomerId = customerId,
        Provenance = provenance, ProofValid = true, PersonalConsentVersion = "synthetic-v1",
    };
    private static async Task AssertSqlStateAsync(AppDbContext db, string state)
    {
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        Assert.Equal(state, Assert.IsType<PostgresException>(failure.InnerException).SqlState);
        db.ChangeTracker.Clear();
    }
}
