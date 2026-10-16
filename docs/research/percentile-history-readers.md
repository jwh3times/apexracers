# Coherent percentile history readers

Implementation for [#428](https://github.com/jwh3times/apexracers/issues/428), following
[ADR 0007](../adr/0007-percentile-history-is-scoped-by-evidence-selection.md), the
[storage cutover](percentile-history-storage.md) and [shared refresh](percentile-history-refresh.md).
This implements controlled synthetic readers and protected Personal transport; it enables no Real
proof, journal, collection or publication adapter and inherits no prior publication-catalog approval.

## One discovered and witnessed history

`Data.PercentileRefresh.ReadDemoHistoryAsync` and `ReadPersonalHistoryAsync` return an opaque
`PercentileHistorySnapshot`. The caller cannot construct its private currentness witness or mutate
its reading, Field, calendar or label collections. Possession is not permission to persist or publish.
Demo analytics resolves the authenticated User's Demo Subject; verified Personal history resolves
current binding, proof, Personal consent and independent journal. Stored claims, legacy percentile
rows and quarantined Uploaded Laps never recover an owner or supply history.

Discovered Demo reads share one synthetic purpose across Users. A Demo-only session advisory gate
428 is acquired before preparation and held across the complete discovered read on the same physical
DbContext session. Concurrent unchanged readers cannot invalidate one another through their own
classified writes. No second pooled connection is held per waiter. This gate is distinct from final
source/catalog coordination: source mutations still reject stale input, without retry or recapture.
Unlock runs unconditionally with an independent 15-second cancellation budget, even when acquiring
the gate races cancellation. Unlock failure clears the connection pool and physically closes the
session before reporting failure; uncertain cleanup is never a successful read. Gate and unlock
statements are included in client command measurements.

Preparation is captured before discovery. Discovery covers the requested Series scope or all Series,
Seasons, Weeks and eligible Cars, including targets with no presentable reading. Sources, catalog
labels and complete Week-window dependencies are read under the same logical preparation. Final
commit takes lock 370 before `SHARE NOWAIT` table locks on Series, Seasons, Weeks, Tracks and Cars.
It compares the full discovered universe and sources, including empty sets, before committing.
New Cars/Weeks, source additions/removals and catalog changes invalidate stale preparation rather
than silently extending it. Catalog contention refuses the read without retrying changed inputs.

The snapshot retains the resulting purpose version after its own commit, plus original authority,
source and catalog witnesses. `SnapshotCurrentAsync` checks those original witnesses again; it does
not bless a changed snapshot through a fresh capture. Unchanged history reads reuse valid classified
rows, markers and original source clocks. Explicit refresh remains replacement; neither operation
renews acquisition/retention clocks or falls back to unclassified history. No migration is added.
The Seeder's legacy claim-based percentile compatibility step is removed; fresh seed production is
classified only. Old rows remain preserved, excluded and subject to existing cleanup.

## Coherent projection and browser display

Percentile calculation, recommendations, Week Detail and Strategy resolve their current Season and
Week from snapshot-witnessed metadata using the existing `SeasonCalendar` policy. Analytics uses
the same discovered history, not a legacy cache joined to separately queried Personal Bests.
Each weekly result carries Week ID/start date, Season/Track identity and names, Personal Best and
evidence, uploaded session type when applicable, its own Field median and canonical selection.
The weekly uploaded-session-type property serializes as a string through its property-local converter;
other enum serialization is unchanged.
Distinct Weeks contribute once across Seasons; equal dates use Week ID as the stable tie-break.

Analytics cards display the last chronological Week's rank, Personal Best, evidence, Track and
median together. Top-level compatibility fields retain the aggregate fastest Personal Best and the best-rank
Week's median; they are not a paired headline or cross-Track pace comparison. Expected Percentile includes eligible Weeks through a requested Week's witnessed
start date; analytics has no new invented current-date filter. Fields below five and missing
Personal Bests contribute nothing. Empty history stays absent rather than becoming zero.
Evidence selection changes clear held browser data before a new response can display it; existing
sensitive-read validity and invalidation rules still apply.

## Protected Personal output

`PercentileHistoryPublication` owns projection, bounded serialization and actual response writes.
The controlled host route derives the authenticated User from JWT `sub`; arbitrary Driver IDs or
internal source/grant identifiers are not a client history capability. Current binding/proof/journal,
operating admission and protected writer admission precede publication. The snapshot's complete
currentness check is the final asynchronous check before the first byte. Refusal returns generic
503 with `no-store`; transport termination precedes the durable writer checkpoint, including drain.
Ordinary startup exposes no positive controlled history route or Real authorization adapter.

This protected synthetic route is separate from ordinary Demo preview personalization and from
prior admitted publication catalogs. Synthetic evidence establishes neither Live eligibility nor
production backup expiry, source acquisition permission or deployed retention guarantees.

## Validation scope

Focused cases cover discovered empty/new Week/Car targets, current source/purpose invalidation,
immutable snapshots, no-op history reads, exact selection/owner separation and source clocks.
API cases exercise actual analytics and shared calendar/projection behavior. Controlled HTTP cases
exercise source addition, expiry and withdrawal/deletion before first byte, plus admitted writer
drain. Restore cases take history before backup, replay independent journal denial and preserve
source clocks after fresh authorization.

Final disjoint post-fix runs passed **18/18** history/gate cases in **2m 38.502s** and **54/54**
protected HTTP, upload-lifecycle and restore cases in **3m 17.938s**, each with zero failures or skips.
Receipts are retained under ignored `TestResults/428-history/gate/history-gate.trx` and
`TestResults/428-history/protected-final/history-protected-final.trx`. The final Release build passed
with zero errors and ten existing warnings in **11.19 seconds**. Actual rebuilt browser acceptance
is recorded separately below.

Earlier API/projection coverage passed 68 tests. Pre-gate runs passed 65 protected/producer/restore
cases and 12 data cases; those overlapping earlier runs are not additional final suite cases. Their
original receipts remain under `focused/` and `discovery/` in `TestResults/428-history/`.

## Discovered-history measurements

After the Demo session gate fix, the dedicated `PercentileRefreshTests` run passed **18/18** with
zero skips in **2m 38.502s** (`TestResults/428-history/gate/history-gate.trx`). It covers concurrent
Demo reads, cancellation/acquisition races, cleanup failure and a two-connection pool limit.
A warmed idempotent history read and its separate final witness were measured for one versus six
Week/Car combinations. Gate and unlock statements are included. The interceptor counts all client
commands and selected rows separately from full-source query/row counts. Process CPU includes the
measured operation, not just arithmetic.

| Operation | Cells | Commands / SELECTs | Source reads / rows | All selected rows | Process CPU ms | Wall ms |
| --- | ---: | --- | --- | ---: | ---: | ---: |
| History read | 1 | 34 / 33 | 2 / 10 | 37 | 16.538 | 40.7073 |
| History read | 6 | 34 / 33 | 2 / 60 | 130 | 16.122 | 39.5832 |
| Final witness | 1 | 15 / 14 | 1 / 5 | 17 | 6.81 | 23.7737 |
| Final witness | 6 | 15 / 14 | 1 / 30 | 54 | 9.1 | 23.914 |

The receipt is `TestResults/428-history/discovered-history-measurements.json`; the earlier pre-gate
receipt is preserved as `discovered-history-measurements-pre-gate.json` in the same directory.
These are small synthetic observations, not a latency or scale-performance guarantee. They do not
replace #427's historical explicit-matrix measurement. With Docker running, reproduce the current data cases:

```bash
dotnet test --project src/ApexRacers.Tests/ApexRacers.Tests.csproj \
  --filter-class ApexRacers.Tests.Services.PercentileRefreshTests \
  --report-xunit-trx --report-xunit-trx-filename history-discovery.trx \
  --results-directory "$PWD/TestResults/428-history/discovery"
```

## Browser acceptance

The initial actual cold-start browser run returned 503 for analytics. Sequential actual Seeder plus
new-User service reads passed, while deterministic overlapping contexts reproduced the shared
SyntheticPreview purpose-version conflict. This motivated the Demo read gate above; the failed
browser evidence is retained rather than treated as passing acceptance. The rebuilt rerun passed
as recorded below.

The existing original sixth Driver privacy browser case passed **1/1**, Playwright exit 0, in
**2.3 minutes** including fresh seeding, against the final Release API/Seeder/Tests and built SPA
15.1.0. It verified cold new-User Demo analytics returns 200 with `no-store`, coherent latest weekly
rank/Personal Best/evidence/Track/median, and actual held replacement clearing old values before the
blended canonical selection displays. Artifacts are retained in `TestResults/428-browser-demo-final`;
the original failed run remains preserved.

Matrix names, collector mapping and admitted catalog receipt remain unchanged. This focused executed
browser case does not replace the full hosted Driver privacy gate, which remains pending, or establish
Live eligibility.
