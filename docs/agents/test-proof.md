# Test proof

How `test-prover` runs one test on its own here, and how it tells a run that tested nothing from a
pass.

## Test commands

- `web/`: Vitest, `src/**/*.{test,spec}.{ts,tsx}`.
  - One file: `npx vitest run <file>`; one test by name: `npx vitest run <file> -t "<name>"`.
  - The count is Vitest's `Tests  N passed (N)` line, which `mutate.mjs` reads already.
  - The scratch worktree links `web/node_modules`.
- repo root: the .NET suite, `src/ApexRacers.Tests/**/*Tests.cs` (xUnit v3 on Microsoft Testing
  Platform).
  - One test class (a file holds one):
    `dotnet test --project src/ApexRacers.Tests/ApexRacers.Tests.csproj --filter-class <class>`,
    with `<class>` fully qualified (`ApexRacers.Tests.Models.FieldPercentileTests`); one test by
    name: the same with `--filter-method <class>.<name>`. A bare class name matches nothing.
  - The count is MTP's `Test run summary:` block, which `mutate.mjs` reads already.
  - The scratch worktree needs nothing: `dotnet test` restores and builds on its own (about 30
    seconds the first time).
  - Not provable without a running Docker engine: the classes in `PostgreSqlCollection` (they use
    `PostgreSqlFixture`) or with `IClassFixture<AzuriteFixture>` start containers, and fail at
    fixture start without one. The `Restore/` tests also need the pinned legacy commit in the
    history (`docs/research/driver-migration-restore.md`), and the `References/` browser rehearsal
    needs a built `web/dist`.
- repo root: `scripts/*.test.mjs`, node's built-in test runner.
  - One file: `node --test <file>`; one test by name:
    `node --test --test-name-pattern="<name>" <file>`, with `<name>` regex-escaped.
  - The count is node's `ℹ tests N` line, which `mutate.mjs` reads already.
  - The scratch worktree needs nothing: the root `package.json` has no dependencies.
- repo root: `scripts/*_tests.py`, Python `unittest`. CI runs only
  `scripts/driver_privacy_contract_tests.py` (Driver Privacy Contract); the others belong to
  research probes.
  - One file: `python3 -m unittest <file>`; one test by name:
    `python3 -m unittest <file> -k <name>`.
  - The count is unittest's `Ran N tests` line, which `mutate.mjs` reads already.
  - The scratch worktree needs nothing.
- `web/e2e/` (Playwright, `*.spec.ts`) and `web/privacy-e2e/` (`playwright.privacy.config.ts`):
  not provable here. They need the full stack at `http://localhost:8080` (`docker compose up`),
  and the privacy suite also a Release build and its synthetic hosts.
