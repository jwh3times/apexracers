---
name: ship
description: Ship the current branch — refresh the docs, write the CHANGELOG entry dated for the version this merge will mint, run the fast checks, push, and open or update the PR. Use when a feature branch is ready for review, or when the user says "ship it", "open a PR", or "push this".
---

# Ship

Take the current branch from "code is done" to "PR is open and green-able", and
make sure the changelog names the version this merge will actually create.

**Announce at start:** "I'm using the ship skill to open a PR for this branch."

> Run the shell commands below through the **Bash tool** (Git Bash) — they are
> POSIX `sh`/`bash`, not PowerShell. `scripts/next-version.sh` and the `$(…)`
> substitutions only work there.

## Why this exists

Every merge to `main` is auto-tagged `v<major>.<minor>.<build>` by
[`.github/workflows/version.yml`](../../../.github/workflows/version.yml). The
major/minor comes from `web/package.json`; the **build auto-increments** for that
line. So the CHANGELOG entry for a branch must be written for **the version its
merge will mint** — `scripts/next-version.sh` computes exactly that, mirroring the
workflow's algorithm.

Because the build increments per line, consecutive merges land on the same
major/minor (e.g. `v0.4.14`, then `v0.4.15`). The **Changelog Version** CI check
(`scripts/verify-changelog-version.sh`) verifies the dated section still matches the
version the merge will mint, and `version.yml` stamps the tag with the **same**
`scripts/next-version.sh` — so prediction and tag share one source of truth. If
another branch merges before yours the number can drift and that check fails, so the
CHANGELOG section this skill writes is **re-numberable** (step 4); step 5 runs the
same check locally before pushing.

## Steps

### 1. Preconditions — stop if any fail

- **Not on `main`.** `main` is protected; work must be on a branch. If on `main`,
  stop and offer to create one (`git checkout -b <topic>`).
- **Feature work already committed.** Run `git status --porcelain`. The only
  uncommitted changes this skill expects to create are the docs + changelog in
  step 6. If there are *other* uncommitted changes, stop and ask the user whether
  to commit them — do not commit unrelated work silently.
- **`gh` authenticated.** `gh auth status` must succeed.

### 2. Compute the target version

```bash
bash scripts/next-version.sh
```

This prints a bare SemVer (e.g. `0.4.14`) — no `v` prefix. It is the single source
of truth; it reads `web/package.json` and the `v*` tags the same way the tag
workflow does. **Do not compute this yourself** — always call the script.

### 3. Refresh the docs

Invoke the `docs-updater` subagent, scoped to **this branch's diff only** — not a
full audit:

```bash
git diff $(git merge-base main HEAD)..HEAD --stat
```

Tell it exactly what changed and let it update the docs it owns (AGENTS.md,
README.md, web/README.md, docs/, the `private/` planning docs, and the
agent specialists — its full matrix is in
`.claude/agents/docs-updater.md`). It also owns
CHANGELOG.md, but **you** write the changelog section in step 4 — tell it to
**leave CHANGELOG.md alone** so you don't fight over the file.

### 4. Write the CHANGELOG entry

The repo's convention is a `## [Unreleased]` section that accumulates undated
bullets during development. Shipping is the deliberate step that **rolls that work
into a dated section** for the version this merge will mint.

Do this:

1. Roll the current `## [Unreleased]` bullets **plus** this branch's user-visible
   changes (derived from the branch diff) into a new dated section inserted
   immediately below `## [Unreleased]`:

   ```markdown
   ## [Unreleased]

   No unreleased changes.

   ## [0.4.14] - 2026-07-15

   ### Added

   - ...
   ```

2. Reset `## [Unreleased]` to the `No unreleased changes.` placeholder.
3. Fix the reference-link footer at the bottom of the file: point the
   `[Unreleased]` link at `compare/v<target>...HEAD` and add
   `[<target>]: https://github.com/jwh3times/apexracers/compare/v<prev-tag>...v<target>`,
   where `<prev-tag>` is the highest existing `v*` tag (`git tag -l "v*" --sort=v:refname | tail -1`).

Rules:

- Date is today, `YYYY-MM-DD`.
- Group under Keep a Changelog headings — `Added`, `Changed`, `Fixed`, `Removed`,
  `Security`. Use **one** heading of each kind per section.
- Describe user-visible behavior and its consequences, derived from the branch
  diff. Not a commit log.
- **Don't fabricate historical sections.** The changelog may jump from an older
  version straight to the target (intermediate build tags are mostly dependabot /
  incremental merges tracked cumulatively under `[Unreleased]`). Document the
  cumulative unreleased work under the target version; do not invent a section per
  skipped build tag.
- **Idempotent:** if you already wrote a section for this version on a previous
  `/ship` of this branch, **rewrite it in place** — never stack a second one. If
  the target version changed since last time (someone else merged first),
  **renumber** the existing section rather than adding a new one.

### 5. Fast checks — refuse to push if any fail

Full test suites and coverage are **not** run here; CI owns them. These are the
cheap gates that catch most mistakes in seconds — including `npm run build` (tsc),
which CI only runs at deploy time, so type/emit errors would otherwise reach `main`
before being caught:

```bash
# web/
cd web && npx prettier --check .   # the exact check CI's Format gate runs
npm run lint
npm run build                      # tsc -b && vite build — CI only does this at deploy
cd ..

# repo root
dotnet build

# repo root — same check the "Changelog Version" CI job runs, so drift is caught
# here instead of on the PR (requires the dated section from step 4 to be written)
bash scripts/verify-changelog-version.sh
```

If any check is red, stop and report — do not push. Fix Prettier with
`npx prettier --write .` from `web/`. If `verify-changelog-version.sh` reports a
mismatch, redo step 4 with the version it names (the prediction drifted).

### 6. Commit the docs and changelog

```bash
git add -A
git commit -m "docs: update docs and changelog for v<version>"
```

### 7. Push and open or update the PR

```bash
git push -u origin "$(git branch --show-current)"
```

Then check whether a PR already exists for this branch:

```bash
gh pr list --head "$(git branch --show-current)" --state open --json number -q '.[0].number'
```

- **No PR** → `gh pr create --base main` with a title and a body derived from the
  changelog section you just wrote. Fill in the repo's PR template sections
  (Summary, Type of change, How was this tested) from the branch's changes.
- **PR exists** → `gh pr edit <number>` to refresh the body. Do not open a second PR.

### 8. Report

Give the user: the PR URL, the version this merge will mint, and anything the fast
checks surfaced. State plainly that the full test suites + coverage run in CI, not
locally — do not imply the branch is verified beyond the fast checks. If the target
version could drift (another branch is also in flight), say so and note the
**Changelog Version** check will confirm it on the PR and the section is
re-numberable on a re-ship.

## Do not

- Merge the PR. `/ship` stops at "PR open".
- Push to `main`.
- Run the full test suites (`dotnet test`, `vitest`) — that is CI's job and it makes
  this skill slow.
- Invent the version number. Always call `scripts/next-version.sh`.
- Fabricate CHANGELOG sections for skipped build tags.
