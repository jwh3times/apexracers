# Integrated Driver Privacy Contract evidence

Implementation for #380 against the accepted `docs/design/driver-acceptance-and-rollout.md` matrix.
The check is an integrated **controlled synthetic** gate. Its green result grants no Live eligibility,
provider processing permission, real ownership proof, production operating authority, deployed erasure,
backup expiry or operational pilot approval.

## Execution and evidence boundaries

`.github/workflows/driver-privacy-contract.yml` reports the **Driver Privacy Contract** context on every
PR to main, on main pushes and on explicit dispatch, without path filters. It runs the complete Release
backend suite (including real PostgreSQL, independent two-host HTTP writers and the migration/physical
snapshot/old-binary rehearsals), followed by the six Chromium cases against the built SPA, two controlled
hosts and the ordinary credential-free Demo host. Both test invocations have no retry policy. General
E2E and coverage remain supplementary checks.

`docs/testing/driver-privacy-matrix.json` maps every one of the 43 matrix IDs to specific method/browser
names, with minimum backend case counts so deleting theory cases cannot silently establish coverage.
The collector resolves actual TRX test definitions/results and original Playwright results; a scenario
label or JSON `Passed` marker cannot substitute for those reports. Every row retains matched execution
IDs/names, bounded fixture scope and unexercised requirements. Backend entries refer to `backend.trx`;
browser entries refer to `browser.json`; the manifest inventories their SHA-256 hashes and the physical
copy, fault, publication, reference, restore and admitted-instance artifacts alongside them.

`python3 scripts/driver-privacy-contract.py start <fresh-directory>` records execution start, current
commit and dirty state, the material source/configuration fingerprint, Release assembly and built-SPA
hashes, provenance, environment and workflow run/attempt. The directory must be empty: prior failure
artifacts are retained rather than overwritten. The collector rejects reports predating the context,
missing/empty/malformed reports, missing matrix cases, insufficient theory executions, any original
failed/skipped/inconclusive result, browser retries, infrastructure errors, changed material source,
and nonzero original process outcomes. It writes `manifest.json` before returning the gate failure.
A job failure cannot be erased by feeding the collector a subsequently passing report.

Each row's `result` describes only its declared controlled synthetic evidence. `fullRequirementResult`
remains `not exercised` where real or deployed requirements are unavailable. The manifest explicitly
records `fullMatrix: not exercised` and `liveEligible: false`, even when `syntheticGate: passed`.
In particular, accelerated deadline/status tests do not observe physical deployed backup/log/temp-orphan
expiry. No complete provider-path or production copy inventory is inferred from one fixture controller.

## Actual admitted useful output

The existing maintainer decision accepted `catalog30-technical-review-v1` without amendment on
2026-10-07 for the exact fabricated #374 four-variant template at
`1ef98d055cb10ed7ff4a84b7426a9197b638c779`. Its sanitized executable receipt is
`docs/testing/driver-catalog-admission-v1.json`; it records the accepted scope/compositions, limits and
hashes of the reviewed template, band/formula and candidate implementation sources. Those four sources
remain byte-for-byte equivalent to the reviewed source. A material change fails the check and requires
renewed review; neither test code nor a new hash is a replacement maintainer decision.

`AdmittedCatalogTests` consumes that receipt, creates a fresh isolated original evidence copy/purpose
and independently current history, and exercises the product protected publication executor through
two actual HTTP processes. Visitor plus SignedIn produces useful ranges and the permitted fabricated
name. Each fixed owner separately produces useful groups and exact percentile
`92.85714285714286`. Owner/non-owner combinations and undeclared catalogs remain closed in that same
continuous global history. Separate owner positive cases each bootstrap a new independent fabricated
instance; they never reset or partition an existing ledger by owner. All contexts retain original
copy/purpose/version/time, source/catalog, proof/consent revisions, epoch, terminal release accounting,
process incarnations and execution date in three `admitted-catalog` artifacts.

The finite catalog/composition adapters remain explicitly synthetic test infrastructure. Their role
is to enforce the recorded bounded decision for the original instance, not to approve risk themselves
or register a production catalog. The receipt supplies the actual technical-risk decision prerequisite;
current proof/consent, original-copy bindings, operating controls and independently continuous history
still own admission and final dispatch. Ordinary startup remains unavailable for Real processing.

Exact owner percentile can reveal Field Size in this fixed fabricated context: a PB below the lap
band yields `100 - 50/n`, allowing `n=7` to be inferred. An omitted count field is not absence of count
information. This inference was accepted only within the literal fabricated evaluation; no future
real-data owner policy inherits that decision.

## Local reproduction

Requires .NET 10, Node dependencies, Python 3, Docker and Chromium. Install/build once:

```bash
dotnet build --configuration Release
npm ci --prefix web
npm run build --prefix web
(cd web && npx playwright install chromium)
python3 scripts/driver_privacy_contract_tests.py
```

Use a new directory on each run, preserving original exit codes even when a suite fails:

```bash
export DRIVER_PRIVACY_EVIDENCE_DIR="$PWD/TestResults/privacy-contract-local"
export PLAYWRIGHT_JSON_OUTPUT_NAME="$DRIVER_PRIVACY_EVIDENCE_DIR/browser.json"
python3 scripts/driver-privacy-contract.py start "$DRIVER_PRIVACY_EVIDENCE_DIR"
dotnet test --project src/ApexRacers.Tests/ApexRacers.Tests.csproj --configuration Release --no-build \
  --report-xunit-trx --report-xunit-trx-filename backend.trx \
  --results-directory "$DRIVER_PRIVACY_EVIDENCE_DIR"
backend_exit=$?
(cd web && npx playwright test --config playwright.privacy.config.ts --reporter=json)
browser_exit=$?
python3 scripts/driver-privacy-contract.py collect "$DRIVER_PRIVACY_EVIDENCE_DIR" \
  --backend-exit "$backend_exit" --browser-exit "$browser_exit"
```

Do not use an older local result to repair a missing current run. Do not rerun a failed security case
until its cause is investigated; retain the original failure and scope any subsequent verification
explicitly. A material change invalidates affected evidence. Private deployed and live operating
procedures remain separately tracked; this check is not an activation instruction.

## Required-check installation

The workflow context must report successfully from the implementing PR before it is added to the
active main/release ruleset. Preserve the existing required contexts and strict up-to-date policy;
append **Driver Privacy Contract** and verify both the API rule and the check run's exact context.
The workflow must be merged before this check can report for ordinary subsequent branches. Until
required-context installation is verified, this issue's live-eligibility prerequisite is incomplete.
The check's synthetic success alone never establishes the separate live gates.
