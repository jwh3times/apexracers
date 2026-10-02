# Driver publication and withdrawal coordination

Reviewed 2026-10-02 for issue #361. Public-safe technical research, not an implementation decision.
Candidate protocols and their consequences below are **engineering inference**. Source facts are
linked directly; no provider contract, credentials, or deployed configuration is assumed.

The [accepted architecture](../design/driver-disclosure-ownership.md) subsequently selects durable
admission/drain, journal-first enforcement, and atomic release accounting. Candidate comparisons
and unresolved implementation feasibility below remain technical inputs, not the current decision
status or a claim that these mechanisms are implemented.

## Accepted scope

The accepted boundaries are Driver Authorization, Driver Publication, and Copy Lifecycle. Primary
PostgreSQL holds durable authority and revisions and participates in shared publication/withdrawal
coordination. Authorized associations and names live separately from name-free shared evidence,
without a complete local Driver mirror. Evidence writes require current checks; references are
opaque and recipient/purpose scoped; Demo/live provenance is explicit. The concrete protocol and
response boundary remain unresolved.

## What the platform guarantees

PostgreSQL MVCC ordinarily lets reads and writes proceed without blocking one another. Read Committed
uses a snapshot for each statement; Serializable orders successful **database transactions**, with
possible transaction retries. Neither establishes authority at a later HTTP send. A revision check
followed by commit and an unlocked send retains a check-then-send race. The last sentence is an
engineering inference from [MVCC](https://www.postgresql.org/docs/18/mvcc-intro.html) and
[transaction isolation](https://www.postgresql.org/docs/18/transaction-iso.html).

Row `FOR SHARE` locks block updates/deletes until transaction end; ordinary reads are not blocked.
Advisory locks are cooperative, and transaction locks end with the transaction; session locks last
until explicit release/session end. PostgreSQL recommends consistent lock ordering to avoid
deadlocks. These facts support candidate coordination, not an HTTP atomicity guarantee.
[Explicit locking](https://www.postgresql.org/docs/18/explicit-locking.html).

ASP.NET `HasStarted` reports whether headers were sent; `StartAsync` makes headers immutable.
`CompleteAsync` flushes remaining response content. These APIs do not document recipient
acknowledgement. Consequently, application-buffered output, dispatch to the response transport,
and recipient-delivered bytes must be distinguished; completion cannot prove browser erasure.
[HasStarted](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.http.httpresponse.hasstarted?view=aspnetcore-10.0),
[StartAsync](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.http.httpresponse.startasync?view=aspnetcore-10.0),
[CompleteAsync](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.http.httpresponse.completeasync?view=aspnetcore-10.0).

## Candidate mechanisms and costs

**Lock through dispatch.** A publisher could obtain shared authority locks, recheck revisions and
recipient/purpose/provenance, then retain ownership through the bounded response writer. Withdrawal
obtains conflicting exclusive locks and commits denial/revision changes after admitted writers finish.
Transaction row locks offer automatic lifetime; transaction advisory locks also cover an absent policy
row. Shared/exclusive advisory functions are available.
[Advisory functions](https://www.postgresql.org/docs/18/functions-admin.html#FUNCTIONS-ADVISORY-LOCKS).
This inference requires every publisher and authority writer to participate. Returning an MVC result
and disposing the lock before its serialization does not satisfy it. Slow recipients consume
connections and delay withdrawal; session locks avoid a long transaction but require reliable release
on the same physical connection. Starvation, lock budgets, and actual writer lifetime need measurement.

**Close admission, then drain.** Durable publication admissions could instead be registered under
shared coordination. Withdrawal first closes new admissions, then waits for existing writers to
finish or be stopped. This avoids holding a transaction throughout dispatch, but introduces durable
work tracking and crash recovery. The initial denial commit and completed withdrawal have different
times: admitted writers might dispatch between them. A product promise tied to the initial commit
cannot silently adopt the later drain boundary. Lease expiry alone proves neither writer termination
nor loss of network access; any lease design needs enforcement at the actual writer, not another
revision check immediately before sending. Feasibility remains unresolved.

Both candidates coordinate ordering only under demonstrated failure assumptions. Neither makes SQL
and HTTP one transaction: SQL rollback cannot retract dispatched bytes.

## Required protocol invariants

1. Fetch provider material and perform expensive computation outside publication locks. Check
   authorization for acquisition first; recheck under coordination before retaining an association,
   writing evidence, or dispatching. Discard stale results. An in-flight fetch is a separate acquisition
   boundary, not proof of publication permission.
2. Determine every Driver/cohort/association/reference dependency of the buffered response.
   Recheck the dependency revisions after locking, including the exact cohort membership used for
   metrics. Locking existing members alone cannot protect newly added members; a cohort coordination
   key or membership revision protocol must cover all mutations. Rebuild or deny on change, rather
   than deleting a name from a metric computed from a now-ineligible cohort.
3. Retain authority through the specified dispatch interval on every API instance. Keep default
   deny, unclaimed Drivers, Demo/live transitions, reassignment, reference resolution, cache hits,
   retries, and copy creation within the same protocol. Prove stable key scope/order and no bypass.
4. Race two instances at admission, provider return, cache commit, serialization, first write, final
   flush, and withdrawal commit. Assert the chosen boundary explicitly; test failure after each step.

These are engineering proof obligations, not a selected schema or implementation.

## Failure and recovery limits

Database session termination releases locks, while transaction/idle timeouts can terminate a session.
An HTTP writer might learn about that loss later. Stop further application writes on uncertainty,
but demonstrate detection/enforcement latency before promising strict exclusion across a partition.
[Session lock release](https://www.postgresql.org/docs/18/functions-admin.html#FUNCTIONS-ADVISORY-LOCKS),
[timeouts](https://www.postgresql.org/docs/18/runtime-config-client.html#GUC-IDLE-IN-TRANSACTION-SESSION-TIMEOUT).
ASP.NET cancellation and `Abort` can stop continued request work; they cannot recall bytes already
handed to transport/proxy buffers or copied by a recipient. That limitation is engineering inference
from [request cancellation](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/use-http-context?view=aspnetcore-10.0#requestaborted).

`COMMIT` makes database changes visible; asynchronous commit can acknowledge changes subsequently
lost in a crash. Verify withdrawal durability settings and resolve an uncertain commit outcome from
authority before reopening publication.
[COMMIT](https://www.postgresql.org/docs/18/sql-commit.html),
[asynchronous commit](https://www.postgresql.org/docs/18/wal-async-commit.html).
Point-in-time recovery can restore earlier authority. Inference: restored revisions alone cannot
prove later withdrawals remain enforced; reopening requires reconciliation or renewed authorization,
including retained copies and references. The recovery protocol remains open.
[PITR](https://www.postgresql.org/docs/18/continuous-archiving.html#BACKUP-PITR-RECOVERY).
