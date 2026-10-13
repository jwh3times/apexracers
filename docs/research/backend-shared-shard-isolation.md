# Shared-collector backend shard isolation audit (#405)

This audit covers two independent Microsoft Testing Platform processes on one Linux runner,
inside one live coverage collection. It follows the resource map in
[the #404 measurements](backend-fixture-cost.md) and the preservation experiment in
[the #408 report](backend-coverage-merge-preservation.md). It establishes the implementation
requirements below; it does not establish hosted timing, cancellation or automatic cleanup.

## Resource boundaries

| Resource | Current boundary | Required shard treatment |
| --- | --- | --- |
| MTP executable and discovery identities | Discovery identifiers depend on the executable location | Copy the complete compiled output to two directories, hash both, discover each copy and map all non-UID metadata back to the canonical inventory. Partition whole classes, with every case included exactly once. |
| PostgreSQL | One `PostgreSqlFixture` container per process; pinned `postgres:18.0-alpine` | Keep two containers, Docker-assigned host ports and distinct Testcontainers sessions. Never share a container, prepared database or schema between producers. |
| Ordinary relational fixtures | Fresh GUID database per fixture operation; `EnsureCreated`, `Pooling=false` | Preserve all current SQL, options, interceptors and per-test creation. |
| HTTP/lifecycle/publication/reference topologies | Fresh primary and independent history databases, epochs and process incarnations; real migrations and child hosts | Preserve both databases, both hosts and the fault-controlled TCP relay within each case. Independent histories stay outside primary snapshots. |
| Physical restores | Actual `pg_dump` and `psql` inside the owned container; unique `/tmp/driver377_<GUID>.sql` | Keep the real commands and original clocks. The path is container-local, so separate containers prevent a shared host-file boundary. |
| Legacy reader | Per-process lazy build of the pinned Git commit, GUID extraction directory, real old API process | Preserve the pinned build and upgrade/old-reader checks; isolate inherited build/temp paths and track its descendants. |
| Artifact files | Environment-controlled privacy, publication and phase destinations | Give each producer its own `DRIVER_PRIVACY_EVIDENCE_DIR`, `PUBLICATION_REHEARSAL_ARTIFACTS`, `APEXRACERS_COST_PROBE_DIR`, results, logs and exit records. |
| SDK/NuGet/build state | Nested legacy builds inherit the producer environment | Give each producer its own `DOTNET_CLI_HOME`, `NUGET_PACKAGES`, `MSBuildUserExtensionsPath` and short owned temp directory. Disable persistent build processes where supported; still verify absence afterward. |
| Process statics | Lazy legacy binary, artifact directory, counters and actor constants are process-local | Keep separate OS processes. Identical synthetic identifiers are safe because the databases remain distinct. |

The ordinary suite creates Testcontainers only through `PostgreSqlFixture`. Lifecycle,
reference, publication and legacy child hosts consume the existing connection; they do not
start additional Testcontainers. The separate `--driver-browser-fixture` route directly creates
another container and uses fixed ports 8185 and 8186. It belongs to the dedicated browser/privacy
workflow and must not be launched twice by the backend shard launcher. The 43-row privacy matrix,
its receipt, browser bootstrap and acceptance collector remain separate and unchanged.

`TMPDIR` and `TESTINGPLATFORM_PIPE_DIRECTORY` must use short, producer-owned paths. #408 retained
an actual MTP startup failure caused by a Unix socket pathname longer than the platform limit.
`Path.GetTempPath()` also places maildrop fixtures and legacy extraction beneath this boundary.
Do not remove these directories until their owned processes have stopped and original diagnostics
have been retained.

## Concrete Docker ownership

The pinned Testcontainers 4.16 implementation assigns a random, process-static
`ResourceReaper.DefaultSessionId`. Cleanup-enabled resources receive
`org.testcontainers.resource-reaper-session=<UUID>`. Ryuk uses the exact name
`testcontainers-ryuk-<UUID>` and auto-removal, but its cleanup configuration does not attach the
producer's session label. There is no session-ID environment setting in this pinned implementation.
Record the actual session before `PostgreSqlFixture` calls its base initialization, then inspect
its concrete resources. These rules follow the pinned
[resource reaper source](https://github.com/testcontainers/testcontainers-dotnet/blob/df0f931ea92111ddd96f96fce22ff85b69a58f64/src/Testcontainers/Containers/ResourceReaper.cs)
and [resource builder source](https://github.com/testcontainers/testcontainers-dotnet/blob/df0f931ea92111ddd96f96fce22ff85b69a58f64/src/Testcontainers/Builders/AbstractBuilder%604.cs).

The installed 4.16.0 package nuspec identifies source commit
`df0f931ea92111ddd96f96fce22ff85b69a58f64`. Its net10 assembly string and that source
both bind the default image to
`testcontainers/ryuk:0.14.0@sha256:7c1a8a9a47c780ed0f983770a662f80deb115d95cce3e2daa3d12115b8cd28f0`.
Capture overrides rather than silently treating another image as this pinned default.

Retain Ryuk as the library's crash-cleanup mechanism. Observe session-labelled containers,
networks and volumes, plus the exact session-named Ryuk container. Record Docker ID, creation
timestamp, labels and image before cleanup. Reinspect ownership immediately before removing a
resource. A before/after Docker delta can detect unexplained resources, but cannot authorize
their removal. Do not prune Docker or remove another producer's resources. Normal fixture
disposal remains the first cleanup path; bounded observation and owned fallback cleanup must
record their distinct outcomes.

## Processes, cancellation and build servers

Current child launchers use ordinary `ProcessStartInfo`; lifecycle and legacy hosts explicitly
kill their trees and await exit on disposal. Legacy build commands likewise kill their tree on
timeout. Successful nested builds can nevertheless leave SDK processes alive: #408 found an
additional dotnet process after the test and collector had exited. Its role was not established.
It was attributed through the producer-owned diagnostic socket and matching PID/start-time
identity, then explicitly terminated. That manual action was not automatic-cleanup evidence.

Launch the collector/launcher under an owned process session and record each producer PID with
its kernel start-time identity. Sample descendant ancestry while parents exist; an orphaned
process may no longer appear beneath the original PID after exit. Owned diagnostic sockets
provide another PID/start-time binding. Revalidate the identity before signalling; PID numbers
and command names alone cannot authorize cleanup. Apply TERM, a bounded wait and KILL only to
the owned group or individually bound survivors, then verify absence. Record failures and
cancellation without substituting another passing invocation.

The #411 cleanup revision keeps always-step follow-up evidence in an exclusively
created `cleanup-followup/` subtree, outside the original artifact manifest.
It preserves the context bytes consumed and their starting hash alongside separate
observations, phase-labelled command/resource journals and the cleanup result.
Concurrent parent finalization does not include these follow-up files in original
hashes; a second invocation refuses to overwrite the first follow-up. Historical
shared journals and their context-bound prefixes remain separate original evidence.

For nested builds, prefer the supported `dotnet build --disable-build-servers` option and
`-nr:false` where the invocation can explicitly supply them. Microsoft documents
[disabling persistent build servers](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-build)
and [disabling MSBuild node reuse](https://learn.microsoft.com/en-us/visualstudio/msbuild/msbuild-command-line-reference).
Separate CLI/temp directories alone do not prove that servers exit. Any environment-based
equivalent must be verified against the pinned SDK and exercised through the actual nested
legacy build. Broad `dotnet build-server shutdown` is inappropriate on a shared development
host because it lacks producer ownership.

## Acceptance evidence still required

The shared VM introduces CPU, memory, disk and Docker contention. Local branch preservation
does not establish that this topology meets the hosted performance budget. Hosted rehearsals
must demonstrate producer failure, interruption after owned resources exist, bounded cleanup,
unchanged source/build/SDK/tool bindings, original outcomes and complete inventory. Preserve
timeouts, cancellation tokens, expiry/drain/retention windows, migrations, two-host cases and
physical restore/legacy-reader checks.

Only successful matched hosted pairs can support the three-run timing comparison. Include all
general and dedicated privacy runner time in the issue's accounting; additional prototype and
failure-rehearsal jobs are costs of the experiment, not omitted production savings. An unexplained
Docker delta, unbound process survivor or missing cleanup observation blocks adoption even if
the product coverage model matches and both original denominators remain above 85%.
