# Merging

What happens between an open PR and a finished merge: the steps `/ship` doesn't cover. Each rule
here was learned from a merge that stalled, went red, or looked done when it wasn't.

## Approval

- **Approval to merge covers only the PR named.** A follow-up PR opened while carrying out "merge it
  and complete the follow-ups" needs its own approval. Open it, wait for CI, report it as ready, and
  ask. Merging to `main` mints a release tag and deploys.
- **Never `--admin`.** A blocked merge is a finding to explain, not a policy to bypass.

## Branch rules

- `main` (and `release/**`) is protected by a repository **ruleset**, not classic branch protection.
  `gh api repos/<owner>/<repo>/branches/main/protection` returns `404 Branch not protected` even
  though the branch is protected. Read the rules from `gh api repos/<owner>/<repo>/rulesets` and
  `/rulesets/<id>`. The ruleset id is in the private companion.
- The required checks are listed under Agent config parity in the project guide. Read the live
  ruleset before claiming a job gates anything: a job existing and a job gating are different facts.
- **A `PUT /rulesets/<id>` replaces the whole ruleset.** Build the payload from a live `GET`, keep
  `name`, `target`, `enforcement`, `bypass_actors`, `conditions`, every entry in `rules` and
  `strict_required_status_checks_policy`, then verify by an independent read. Editing the ruleset is
  the maintainer's decision.
- The ruleset is strict about up-to-date branches, and repository auto-merge is off. Every merge
  leaves the other open PRs behind `main`, and each update restarts `Test` and
  `Driver Privacy Contract`. **Budget about 35 minutes of CI per PR in a queue.**
- The private companion has no rulesets (unavailable on the free plan for a private repository), so
  its `main` takes direct commits and has no PR or CI flow.

## Watching CI

- **Watch by commit SHA.** Right after a push, `gh pr checks` can report on the previous head. Compare
  each run's `headSha` with the PR's current `headRefOid`.
- **Force the refspec when fetching a PR head** (`+pull/<n>/head:refs/remotes/pr/<n>`). After a
  force-push, such as a Dependabot rebase, a fetch without `+` fails silently, and a guard comparing
  against the old head reports nonsense. Prefer asking GitHub (`mergeStateStatus`, `headRefOid`).
- **Driver Privacy Contract hides its failures from `--log-failed`.** Its test steps record exit codes
  and carry on; only the final evaluation step fails, so `gh run view <id> --log-failed` shows only
  the summary. That also means a `--log-failed` search for past failures always comes back empty.
  Download the full log (`gh run view <id> --log > dpc.log`) and grep it for `✘`, `failed` and
  `Error:`.
  - **"Install Chromium" fails at about 11 minutes:** the runner couldn't reach the Ubuntu mirrors.
    Re-run.
  - **A test step fails at about 28 minutes:** a real case failed. Investigate before any re-run.

## When a PR is BLOCKED with every check green

Check these causes, in order:

1. **The main CI workflow never ran on the head commit.** On a Dependabot PR, `deploy.yml` (Format,
   Lint, Test) sometimes never starts, so its required checks never report. `@dependabot rebase`
   pushes a new head and CI runs.
2. **A CodeQL language's upload never registered.** The `Analyze (<lang>)` job passes but its log
   ends `Analysis upload status is pending … Continuing.` The tells:
   - the `CodeQL` check run is `neutral`, titled `1 configuration not found`;
   - `gh api "repos/<owner>/<repo>/code-scanning/analyses?ref=refs/pull/<n>/head"` lists fewer
     languages than usual.

   These runs can't be re-run, so the fix is a new push. In a queue, merge another PR first, since the
   blocked one then needs `gh pr update-branch` anyway. (On an untouched Dependabot head,
   `4 configurations not found` is normal and doesn't block.)
3. **The branch is behind `main`.** Run `gh pr update-branch`, then wait for the new run.

## A red CodeQL check

The aggregating `CodeQL` check gives no detail. Fetch the alert:
`gh api "repos/<owner>/<repo>/code-scanning/alerts?ref=refs/pull/<n>/head&state=open"`.

`cs/cleartext-storage-of-sensitive-information` fires on the **identifier's name**. A constant called
`AccountPrimaryKey` holding a constraint name is enough to trigger it, and the same goes for
`*Password`, `*Secret` and `*Token`. If the value isn't a credential, rename the identifier and say so
in the commit. Don't dismiss it: a dismissal doesn't survive the name reappearing.

## A red check no branch caused

- **A check that goes red on every open PR at once is usually a new third-party advisory**, such as
  `npm audit (web)` failing on a dependency with no patched version. It still blocks
  (`docs/agents/verification.md`), so merging past it takes an explicit maintainer waiver for each
  PR. Record the waiver as a PR comment giving the cause, the exposure and that every other check is
  green, and track the advisory in an issue.
- **Before pinning or overriding the dependency, check whether the advisory was withdrawn:**
  `gh api advisories/<GHSA-id> --jq .withdrawn_at`. A withdrawn advisory drops out of the audit, and
  the check clears with no dependency change.
- **Security Audit doesn't run on a push to `main`.** `.github/workflows/security-audit.yml`
  triggers only on `pull_request`, its weekly `schedule` and `workflow_dispatch`. To show the audit
  is green on `main` after a merge, run `gh workflow run security-audit.yml --ref main` and watch
  that run by its head SHA.

## Changelog and release after a merge

No branch names a version (`Scheme: stamp-on-tag`, ADR 0008). A branch's entries live in its own
`changelog.d/<branch-slug>.md` fragment, so open PRs never conflict over `CHANGELOG.md` and merge
in any order. `Verify changelog version` checks the fragments instead of a predicted version, and
Dependabot PRs pass it like any other: with no fragment, the release takes its entry from the PR
title.

After each merge, `Version` (`.github/workflows/version.yml`) stamps the merge's section into
`CHANGELOG.md`, tags the merge commit and creates its GitHub Release. The section arrives in a
`chore(release): v<x.y.z> [skip ci]` commit pushed by the release GitHub App, the ruleset's one
bypass actor. That commit runs no workflow, so nothing deploys it. To confirm a merge's release,
find the `Version` run for the merge commit's SHA and check that the tag points at that commit.

The stamp commit moves `main` again, about a minute after the merge. Because the ruleset is strict
about up-to-date branches, every open PR is behind until it is updated past the stamp commit. Wait
for the `Version` run to finish before `gh pr update-branch`, or the branch needs a second update
and a second CI run. Use `git merge origin/main`, never a rebase, on a pushed branch.

## Release impact

`/ship`'s rubric calls "an incompatible change to a public API" major. This repository's bar, read off
its own changelog, is narrower: **major means an existing client or operator must now do something
differently**, not that a consumer could notice a difference.

- `6.0.0`–`9.0.0` each forced a client or operator edit.
- `8.0.12` removed a field from an auth response and shipped as a build. That is the calibrating
  precedent; dropping a status code, a response field or a header is usually a build.

Cite the precedent in the PR body. If the honest answer is ambiguous, ask rather than inflate. Say
plainly in the notes what changed for consumers even when the bump is a build, and don't label a
bullet **Breaking** while the same bullet says no action is needed.

## Dependabot PRs

- **Merge them one at a time.** Two npm PRs both rewrite `web/package-lock.json`, so after one merges
  `gh pr update-branch` on the other fails with conflicts. Comment `@dependabot rebase` and wait for
  its full CI run.
- **`@dependabot rebase` can close the PR** and open a replacement that bundles more updates. Watch
  the PR's `state` as well as its head, and look for the replacement in `gh pr list` after a close.
- **A real failure caused by a bump** (for example a new lint rule) is fixed by pushing a commit to
  the Dependabot branch.

## Security advisories

Open findings are draft repository security advisories, not issues or files. A public issue would
disclose a finding before its fix ships, so track the fix as a draft item on the board instead.

- **Creating one:** `POST repos/<owner>/<repo>/security-advisories` requires a `vulnerabilities`
  array, even for a deployed app, and takes severity `medium`, not `moderate`. Pass the payload as a
  file with `--input`.

  ```json
  "vulnerabilities": [{ "package": { "ecosystem": "other", "name": "ApexRacers.Api" },
    "vulnerable_version_range": null, "patched_versions": null,
    "vulnerable_functions": ["AuthService.RegisterAsync"] }]
  ```

- **When the fix merges:**
  1. `GET` the advisory, append a `## Resolution` section to its `description` (don't rewrite earlier
     sections), and `PATCH` the description back.
  2. `PATCH state=closed`. Never `published`: there are no downstream consumers to warn, and
     publishing would disclose the finding.
  3. Set the board item to Done.

  The Resolution states the PR number and merge SHA, the companion PR, that checks passed, the
  released version, whether its deploy succeeded, and any migration, operator step or open human
  follow-up. It also records any departure from the advisory's recommended fix and why (see
  `docs/agents/verification.md`).
- **When only some findings ship:** append `## Implementation progress` instead, leave the advisory
  `draft`, set its board item to In Progress, and open a tracked issue for the rest. Say outright which
  findings are **not** fixed.
