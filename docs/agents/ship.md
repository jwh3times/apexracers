# Ship

## Fast checks

- `web/`: `npx prettier --check .` (the required **Format** check), fixed by `npx prettier --write .`
- `web/`: `npm run lint`
- `web/`: `npm run build` — `tsc -b && vite build`, which CI runs only at deploy time
- repo root: `dotnet build`
- repo root: `npm run sync:agents:check` (the required **Verify generated agent config** check,
  from the Agent Config Sync workflow), fixed by `npm run sync:agents`, committing both sides
- repo root: `npm run lint:agents` (same check)
- repo root, only when `scripts/` changed:
  `node --test scripts/sync-agents.test.mjs scripts/lint-agent-neutrality.test.mjs` (same check)

`npm run lint` covers the required **Lint** check. The required **Test** check (the .NET and Vitest
suites with coverage) and **Driver Privacy Contract** (a full build with Playwright privacy cases)
run only in CI.

## Issue link

- **Branch pattern:** `<type>/<issue#>-<slug>`, types `feat`, `fix`, `refactor`, `test`, `docs`,
  `ci`, `research`, `investigate`, `chore`. Exempt work drops the number (`<type>/<slug>`);
  Dependabot keeps its own names, and `/prototype` its `prototype/<name>`.
- **Link line:** `Closes #N` when the PR finishes issue `N`; `Part of #N` for one slice of a spec
  that stays open. It goes above `## Summary`; other related issues get `Refs #N` lower in the body.
- **Waiver line:** `Flow waived by the user: <reason>`.
- **Exempt:**
  - dependency bumps
  - changes touching only docs and agent config
  - release prep
- **Defined in:** `docs/agents/dev-flow.md`.
