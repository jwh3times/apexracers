# Bounded Driver browser display

Public #376 implements BROWSER-01–03 at browser resource, actual protected HTTP and built-SPA
seams. This is synthetic implementation evidence, not Real authorization or a complete rollout
gate. Ordinary startup retains unavailable proof/journal/catalog adapters.

`useDriverResource` owns every sensitive page read: selection changes discard the old resource
before rendering; current protected reads repeat every 15 seconds; both wall and monotonic clocks
cap display at 30 seconds from check **start**. A delayed response cannot renew that deadline.
Expiry is irreversible for that snapshot, including a subsequent wall-clock rollback. Old success
and error generations cannot publish, and failure cannot substitute an old-data fallback.
HTTP refresh retries remain bound to the request's original session owner, so a withdrawal
cannot replay under a different User after a cross-tab session change.

Offline, hidden, pagehide, focus, pageshow, reconnection and session-owner changes invalidate
display. Resumption checks current browser connectivity even when a suspended page missed its
online event. Cross-tab signals contain only an invalidation signal. A pending own-User withdrawal
stores only its operation UUID and personal/sharing scope, keyed by User; it is a veto, never
permission or a Driver-data cache. Reload keeps the display closed until the original operation
is confirmed. All application HTTP reads request `no-store`; sensitive server successes and
denials also use `Cache-Control: no-store`.

The Dashboard's protected personal card and Compare's consented shared card use the existing
owned publication executor, original source receipt, exact synthetic catalog review and combined
release accounting. Neither Customer ID nor name resolves a shared reference. A selection retains
only its opaque reference; names and results come from bounded reads. Ordinary startup cannot
select the test catalog. Authenticated withdrawal resolves the caller's own stored association,
replays the same operation against its original association and uses the independent journal-first
authorization transition. Sharing loss retains personal access. Completion reports durable intent
and writer drain separately; live erasure and backup expiry remain unverified. Delivered/exported
bytes are outside recall guarantees.

## Reproduce

Docker, .NET from `global.json`, Node and the pinned frontend dependencies are required:

```bash
dotnet test --project src/ApexRacers.Tests/ApexRacers.Tests.csproj --filter-namespace ApexRacers.Tests.References
cd web
npm ci
npm run test
npm run lint
npm run build
npx playwright install chromium
npx playwright test --config playwright.privacy.config.ts
```

The dedicated test executable bootstraps PostgreSQL 18, actual migrations, two protected Kestrel
processes on independent host incarnations, separate primary/history databases, and the built
`web/dist` SPA. The bounded browser template is explicitly `controlled-browser-template-v1`,
separate from #375's `controlled-reference-template-v1` review. Both use exact fabricated scope,
source, receipt and schema values; neither is general catalog admission. A third process uses
**ordinary API startup**, its own database and `Seeder --ci --demo`, with service/provider/cloud
credentials disabled, to prove useful Demo without Real authorization. All fixtures use loopback;
test-mode flags exist only in the Tests executable.

Built-browser cases cover two actual pages, a held before-journal withdrawal (immediate own-User
clearing while a direct API probe still succeeds), delayed first and replacement reads, obsolete
success, exact deadline expiry, offline/reconnect, focus/pagehide/pageshow, source uncertainty,
reload/pending retry, current shared references and private Follows, sharing-only loss preserving
personal access, real no-store headers and useful Demo profile/progression. Resource/rendered tests
add stale error, selection replacement, clock rollback and a missed-online-event recovery.
The E2E workflow runs this separate configuration after ordinary E2E; this does not promote E2E
to a required privacy gate or complete #380.

Application validity is bounded when JavaScript is executing. A fully frozen browser/OS cannot
erase already-painted pixels while no code runs; resumption discards state before fresh display.
These checks establish neither production backup lifetime nor removal outside application control.

Local verification on 2026-10-08 passed 59 Reference HTTP cases, all 814 frontend tests,
all six built-SPA cases, production build, Oxlint, whole-web Prettier, generated-agent parity
and whitespace checks. The two new personal/withdrawal HTTP cases passed again after the
final service review. Full integrated backend acceptance remains separate from this slice.
