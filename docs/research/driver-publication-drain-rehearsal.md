# Driver publication drain: executable rehearsal

Issue [#368](https://github.com/jwh3times/apexracers/issues/368) implements a **synthetic transport
feasibility rehearsal**, reviewed against the accepted [ownership architecture](../design/driver-disclosure-ownership.md)
and [acceptance matrix](../design/driver-acceptance-and-rollout.md). It is a test fixture, not the
production authorization, journal or publication implementation. The ordinary API has no new route,
permission, schema or migration from this work.

## Topology and observable boundary

Each scenario uses a fresh database in the pinned `postgres:18.0-alpine` test container and two
separate .NET/Kestrel processes listening on ephemeral loopback ports. Both processes run the test
assembly's isolated synthetic host, with independent non-pooled PostgreSQL connections. The
publisher's database traffic traverses a fault-controlled TCP relay; the other host reaches the same
database directly. No TestServer, substituted response stream, production startup or provider data
is used. A restart creates a third process with a new incarnation.

The test-only authority row serializes admission against closure. A durable admission remains
pending until its protected executor records terminality. Withdrawal commits closed admission and
reports **202 pending** until every admission has a durable terminal checkpoint; only then does it
report **200 completed**. These are fixture response codes, not selected production endpoints or
wire contracts. Previously admitted writers may finish before completion, after admission closes.

The executor owns every application write, awaits actual Kestrel writes/flush/completion, and has no
detached write tasks. Client cancellation unwinds the held executor and aborts its response before
checkpointing. A known ended executor can retry a lost checkpoint through a new database connection.
An unknown or still-running executor cannot use that recovery route.

Deterministic gates hold before admission, after durable admission/before the first write, between
the two flushed chunks, and after transport completion/before durable terminal recording. The test
supervisor controls each gate over actual HTTP and checks actual client bytes/statuses and pending
outcomes through the other host. Timeouts bound failed cases; elapsed time is never terminal proof.

## Scenarios and observed outcomes

The executable suite contains 12 cases. The cases establish the following outcomes in this topology;
retain the generated artifacts/TRX for the exact code version and run. They do not mark the complete
production acceptance matrix passed.

| Scenario | Expected and observed contract |
| --- | --- |
| Healthy publication | Both synthetic chunks arrive; terminal checkpoint permits withdrawal; subsequent publication is forbidden. |
| Withdrawal before admission | Closure can complete while the request is held; releasing it produces no protected body and no new admission. |
| Withdrawal after admission/before dispatch | New admissions close, but completion is pending until the held writer finishes and checkpoints. |
| Withdrawal during streaming | Previously admitted output can finish before acknowledgment; completion stays pending while the stream is held. |
| Writer ended, checkpoint delayed | Even a completed transport does not substitute for its missing durable checkpoint. |
| Writers on both instances | Pending count transitions from two to one to zero as each actual writer checkpoints; one local writer's completion is insufficient. |
| Client cancellation | Cancellation stops the held executor before checkpointing; releasing the old gate cannot resume its application writes. |
| Actual TCP reset | The client receives the first chunk and resets its socket; the held writer observes disconnection, aborts and checkpoints without a final chunk. |
| Expired lease | Expiry changes no terminality outcome; the living held writer can still dispatch. Early checkpoint recovery is rejected. |
| PostgreSQL session loss | Terminating the actual writer backend releases its measured advisory lock while its HTTP process can still write both chunks. The failed durable checkpoint keeps withdrawal pending until the ended executor recovers it. |
| Asymmetric database-link partition | Existing publisher database connections are cut and new connections refused; HTTP remains alive. The writer can finish, but checkpoint recovery returns unavailable and withdrawal remains pending until link restoration **and** terminal checkpoint recovery. |
| Process restart | Killing/waiting for the exact old process and starting a new incarnation does not automatically complete old admissions, even after lease expiry. Admission stays closed. Only a separately recorded supervisor-confirmed exit permits old-incarnation reconciliation. |

The advisory lock is deliberately measured as an **insufficient** signal. Admission/withdrawal
terminality does not rely on its continued ownership. The session-loss case is an executable
counterexample to treating a released SQL lock as stopped HTTP output.

## Reproduce and retain evidence

With the .NET SDK and a running Docker engine, from the repository root:

```bash
dotnet test src/ApexRacers.Tests/ApexRacers.Tests.csproj \
  --configuration Release \
  --filter-class ApexRacers.Tests.Publication.PublicationDrainRehearsalTests \
  --report-xunit-trx --report-xunit-trx-filename publication-rehearsal.trx \
  --results-directory ./TestResults --output Normal
```

Scenario JSON files are written under `TestResults/driver-publication-rehearsal/<unique-run>/`.
`PUBLICATION_REHEARSAL_ARTIFACTS` can select another artifact parent directory. Separate run
directories prevent a previous successful scenario from impersonating a later unexecuted case.
Keep the TRX as the authoritative complete test-run result; setup failure or an absent artifact
does not pass a case. The normal CI backend artifact upload already includes `TestResults/`.

Each JSON identifies the scenario/result, execution time, assembly informational commit version,
runtime/platform, synthetic consent/provenance, absence of catalog admission, PostgreSQL image,
host process/incarnation/loopback addresses, relay topology, controlled faults, writer observations
and withdrawal outcomes. Connection strings, credentials and real Driver data are excluded.
Artifacts are regenerable and are not committed. An uncommitted build's assembly commit identifies
its base; use a clean committed build for durable review evidence and retain the corresponding diff
when investigating work in progress.

The test executable selects the synthetic host only for its exact internal host argument. Ordinary
test/discovery/coverage invocations retain the pinned xUnit/Microsoft Testing Platform runner and
registered extensions. The host loads none of the application's cloud/provider startup and binds
only loopback. It refuses a database outside the isolated rehearsal namespace.

## Feasibility result and remaining proof obligations

The candidate **close admission, then retain pending work until actual terminal evidence** is viable
for these controlled cooperative executors. A durable checkpoint after all protected write work
ends can support acknowledgment. A trusted supervisor's confirmed exit of the exact incarnation
can support reconciliation in this local single-machine experiment. An expired lease, lost session,
released lock, cancellation request alone or a replacement process cannot.

This result does not establish a production supervisor/access contract or guarantee detection and
forced termination across cloud/network partitions. Unknown writers must remain pending; a
production design that needs bounded successful acknowledgment under those failures still needs
verified terminality evidence. [#370](https://github.com/jwh3times/apexracers/issues/370) must integrate
the actual protected result/executor with journal-first lifecycle transitions and every affected
legacy path, preserving that pending behavior. A process heartbeat or PID without incarnation-bound
trusted exit evidence is insufficient.

The fixture does not exercise the independent journal, proof/consent ownership, cohort/catalog
composition, real copy cleanup, mixed-version production writers, full database outage/restore,
production proxies, arbitrary serializers/body sizes or HTTP/2 multiplexing. These remain applicable
integration/deployment proof obligations in #370, #374, #377, #378 and #380. HTTP-01–03 has candidate
transport evidence here; AUTH-04–05 and MIGRATE-01 receive partial feasibility input, not full passes.

Application dispatch, transport completion and recipient receipt are separate boundaries.
Already queued/delivered/exported bytes are outside the recall guarantee. The platform's
[response completion API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.http.httpresponse.completeasync?view=aspnetcore-10.0)
flushes response content; it is not recipient erasure evidence. The linked
[coordination research](driver-publication-coordination-2026-10-02.md) explains the platform facts
and candidate failure assumptions. No outcome here authorizes live activation or certifies
production privacy/retention claims.
