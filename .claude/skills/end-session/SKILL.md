---
# GENERATED — DO NOT EDIT. Source: .agents/skills/end-session/SKILL.md. Regenerate: npm run sync:agents
name: end-session
description: End a work session cleanly — capture what was learned into memory, bring GitHub issues and the private/ planning docs up to date, and clean up the local workspace. Use when the user says "end session", "wrapping up for the day", "clean up before I stop", or invokes /end-session.
disable-model-invocation: true
---

# End session

Close out the day's work so the next session starts from an accurate picture instead of
reconstructing one. In the user's words, the job is:

> clean up the local workspace, update any private/ docs and/or github issues that need it from this session.

Plus the two things that are easy to lose because they live outside the repo: **memory** (what was
discovered) and **issue tracking** (what the session actually moved).

**Announce at start:** "I'm using the end-session skill to close out this session."

> Run the shell commands below through the **Bash tool** (Git Bash) — they are POSIX `sh`/`bash`,
> not PowerShell. The `$(…)` substitutions and `git`/`gh` pipelines only work there.

## What this is not

- **Not `/ship`.** This skill does not evaluate SemVer impact, write a dated CHANGELOG section,
  push, or open a PR. If a branch is finished, run `/ship` **first**, then this. If a branch is
  mid-flight, leave it mid-flight — record where it stands and stop.
- **Not `docs-updater`.** That agent owns the doc matrix for a *shipped change*. This skill covers
  what a session learned that no diff records — a blocker's real cause, a decision, a dead end, a
  correction to a planning assumption.
- **Not a `git clean -xfd`.** Several ignored paths here are irreplaceable (below).

## Steps

### 1. Take stock of the session

Before touching anything, write out — for yourself and then for the user — what this session
actually did. Distinguish four buckets, because they route to four different places:

| Bucket | Goes to |
| --- | --- |
| Durable facts about *how to work in this repo/environment* | Memory (step 2) |
| Work started, finished, blocked, or newly discovered | GitHub issues (step 3) |
| Planning-state changes, findings, maintainer-only detail | `private/` docs (step 4) |
| Files, containers, worktrees, branches left behind | Workspace cleanup (step 5) |

Ground it in evidence, not recollection:

```bash
node scripts/repository-status.mjs                  # public + optional private worktree state
git status --porcelain                              # uncommitted work
git log --oneline main..HEAD                        # this branch's commits
git branch --show-current
gh pr list --author @me --state open --json number,title,headRefName
```

If the session produced nothing in a bucket, say so and skip that step — an empty step is a valid
outcome, an invented one is not.

### 2. Update memory

Memory lives outside the repo, one fact per file, in this project's memory directory
(`~/.claude/projects/<project-slug>/memory/`, where the slug is the repo path with separators
replaced by `-`). `MEMORY.md` in that directory is the index loaded into every session — one line
per memory, never memory content itself.

Ask of each candidate: **would a fresh session in this repo get this wrong without the note?**

Save it when the answer is yes and it is *not* already recorded by the repo. Good candidates from
sessions here have been environment quirks (`jq` absent from Git Bash), tooling traps (the frontend
gate needing `npm run build`), and process facts (`main` is protected by a ruleset, so the
`/branches/main/protection` API 404s).

Do **not** save what the repo already records: architecture, service responsibilities, test
commands, and schema all live in `AGENTS.md`, `CONTEXT.md`, `docs/adr/`, and the `.claude/agents/`
specialists — a memory duplicating those goes stale the moment the file changes. Session-local
detail (what a specific PR did) belongs in the issue and `CHANGELOG.md`, not memory.

Then:

- **Existing memory covers it?** Update that file rather than adding a near-duplicate.
- **Session proved a memory wrong?** Correct or delete it — a stale memory is worse than none.
- Every new or renamed file needs its `MEMORY.md` pointer line added or fixed.

### 3. Update GitHub issues and the project board

Issues are the tracker (`jwh3times/apexracers`), driven with `gh` — the full command vocabulary is
in `docs/agents/issue-tracker.md`, and the label strings are in `docs/agents/triage-labels.md`
(`needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`). Use those files
rather than inventing commands or labels.

Issues carry the *work*; the private [project board][board] carries its *state* — `Status`
(`Todo` / `In Progress` / `Blocked` / `Parked` / `Done`) and `Blocked by`. There is no Markdown
backlog: the board replaced `private/ROADMAP.md` on 2026-09-04. Every issue this session opened
belongs on it.

[board]: https://github.com/users/jwh3times/projects/2

Start from what is open, so nothing the session touched is missed:

```bash
gh issue list --state open --json number,title,labels \
  --jq '[.[] | {number, title, labels: [.labels[].name]}]'
```

For each issue this session touched:

- **Shipped** (merged, or a PR is open that closes it) — comment with the outcome and what changed
  about the understanding of the problem, not just "done". Close it if merged; if the PR is only
  open, leave the issue open and say which PR carries it.
- **Advanced but unfinished** — comment with where it actually stands and what the next concrete
  step is. Future-you reads this comment cold.
- **Blocked** — comment with the blocker and, if it is a *new* blocker, whether it is the standing
  iRacing-credentials one (the project board's *Blocked by* field names the standing blockers) or
  something new worth its own issue. Set the board item's Status to `Blocked` and its *Blocked by*
  to the matching reason.
- **Understanding changed** — if the session showed the issue body is now wrong or under-specified,
  correct the body or comment the correction. An issue that describes the wrong problem costs more
  than a missing one.
- **Newly discovered work** — open an issue rather than leaving it in a doc or a code comment.
  Label it per the triage vocabulary, then add it to the board
  (`gh project item-add 2 --owner jwh3times --url <issue-url>`) and set its `Status` and
  `Blocked by`. An issue that never reaches the board is invisible to the next session.

Do not close an issue on the strength of an unmerged branch, and do not bulk-relabel issues this
session never touched — that is `/triage`'s job.

### 4. Update the `private/` docs

`private/` is ignored by the public repository because it is an optional standalone companion.
When `private/.git` exists, inspect its content and Git state from that root; outer `git log`,
`git show`, and `git diff` remain blind to it. When the companion is absent, report that private-doc
reconciliation could not run and do not create an ignored orphan directory.

| File | Update when |
| --- | --- |
| `private/PRD.md` | A **feature-level** change — capability added, planned feature cancelled, user story revised. Not implementation detail. |
| `private/ops/azure-deployment-runbook.md` / `private/ops/iracing-rollout.md` | Deployment resources or commands changed, or a rollout gate advanced. |
| `private/reviews/architecture-findings.md` | A disposition from the 2026-08-08 review was actioned or overturned. This is a record, not a live backlog. |
| `private/iracing-api-response-objects/` | A **new** payload shape was captured. Add it — never delete or overwrite one (step 5). |

`private/archive.md` is **frozen** — never append to it. Completed work is recorded by closed issues
plus `CHANGELOG.md`.

**A security finding is never a Markdown file and never a public issue before the fix ships.** Open a
repository security advisory on the public repo (`gh api --method POST
repos/jwh3times/apexracers/security-advisories`), which stays private while in draft, and track the
fix as a **draft** item on the project board so the board stays useful without disclosing anything.
`private/archive/security-audit-2026-06-23.md` is the retired audit record, not a live findings list.

If `/ship` already ran `docs-updater` on this branch, verify rather than redo, and fill only the gaps.

Private changes belong to a separate commit in the companion repository. This skill does not
silently commit or push either repository: report the private diff and ask before committing it. An
open public PR is not shipped; leave board items open until merge.

### 5. Clean up the local workspace

Enumerate before deleting:

```bash
git status --porcelain --ignored=matching | grep -v 'node_modules\|/bin/\|/obj/'
git worktree list
git branch --merged main | grep -v '^\*\|main'
docker compose ps
```

**Safe to remove** — regenerable build/test output:

- Root coverage scratch dirs: `coverage*/`, `coverage-report/`, `TestResults/`
- Frontend output: `web/coverage/`, `web/dist/`, `web/test-results/`, `web/playwright-report/`,
  `web/blob-report/`
- Stray temp scripts and probe files written during the session — including anything dropped at the
  repo root that should have gone to the session scratchpad
- Finished git worktrees under `.claude/worktrees/` (`git worktree remove <path>`, then
  `git worktree prune`) and local branches already merged into `main`

**Never remove without an explicit request** — none of these are regenerable here:

- the `private/` companion worktree in any form, especially its captured response objects
- `.env`, `*.secrets.json`, `.claude/settings.local.json`, `docker-compose.override.yml`
- Any branch that is unmerged, or a worktree with uncommitted changes

So: **no bare `git clean -xfd`** — it would take `private/` and `.env` with it. Delete by explicit
path, and confirm the list with the user first.

Then check the two things a session commonly leaves inconsistent:

```bash
npm run sync:agents -- --check   # agent/skill config drift — CI fails the PR otherwise
docker compose ps                # local stack still running?
```

If `--check` reports drift, run `npm run sync:agents` and commit **both** sides. Ask before
stopping containers — a running Postgres may be deliberate.

Finally, account for uncommitted work. Do **not** silently commit or discard it: report it and ask.
If it is worth keeping but not finishing, offer to commit it on its branch or stash it with a named
message.

Run `node scripts/repository-status.mjs --check` last. A missing companion is success; an installed
public or private worktree that is dirty, has no upstream, or is ahead/behind is not portable. Name
the exact repository state instead of calling the session synchronized.

### 6. Report

Close with a short, honest summary:

- Memories added / updated / deleted, with the one-line reason for each
- Issues commented, closed, relabelled, or opened — with numbers
- `private/` docs updated, their separate commit/push state, or that the companion was absent
- What was deleted from the workspace, and what was deliberately left (and why)
- **Anything left open**: uncommitted changes, unpushed branches, an open PR awaiting review, a
  running container, a decision the user still owes

State plainly what was *not* done. An accurate "these three things are still open" is the point of
the skill; a tidy summary that hides them defeats it.

## Do not

- Merge PRs, push to `main`, or run `/ship`'s release steps.
- Roll `## [Unreleased]` into a dated CHANGELOG section — that is `/ship`'s deliberate step.
- Delete `private/`, `.env`, local settings, or the captured iRacing response objects.
- Run `git clean -xfd` or any blanket ignored-file delete.
- Commit or discard uncommitted work without asking.
- Invent memories, issue comments, or archive entries to make a bucket look non-empty.
- Cite outer-repository Git history as evidence about the nested private repository.
