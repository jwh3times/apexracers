# Ship

## Fast checks

- `web/`: `npx prettier --check .` (the required **Format** check), fixed by `npx prettier --write .`
- `web/`: `npm run lint`
- `web/`: `npm run build` — `tsc -b && vite build`, which CI runs only at deploy time
- repo root: `dotnet build`
- repo root: `npm run sync:agents:check` (the required **Agent Config Sync** check), fixed by
  `npm run sync:agents`, committing both sides
- repo root: `npm run lint:agents`

The required **Test** check (the .NET and Vitest suites with coverage) runs only in CI.

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
