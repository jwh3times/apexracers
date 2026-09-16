---
name: handoff
description: Write a handoff document to the synced Proton Drive Handoffs folder, register it in handoff_map.json, then close the session with end-session.
argument-hint: "What will the next session be used for?"
disable-model-invocation: true
---

# Handoff

Hand the work to a fresh session on another machine (this Windows PC or the Fedora PC). The
handoff document lives in the synced Proton Drive Handoffs folder, and `handoff_map.json` beside it
names each project's **active handoff** — the document `/lets-go` resumes from on the other machine.

**Announce at start:** "I'm using the handoff skill to hand this session off."

> Run the shell commands through the POSIX shell (Git Bash on Windows). The map is read and written
> only through `node scripts/handoff-map.mjs`, never with `jq` or by hand.

## How the Handoffs folder reaches this machine

Two transports, decided by what is installed:

- **Desktop client** (Windows) — the Proton Drive client keeps
  `~/Proton Drive/<account>/My files/Documents/Handoffs` in sync on its own. Writing into that folder
  is the whole sync; the CLI steps below are skipped.
- **CLI mirror** (Fedora, where no client exists) — `proton-drive` (the Proton Drive CLI) is on
  `PATH` and `HANDOFFS_DIR` names a local mirror folder. Nothing syncs by itself: each **Pull** and
  **Push** block below is run explicitly, and the cloud folder is always
  `/my-files/Documents/Handoffs`. A CLI reply of `You need to login first` means `proton-drive auth
  login` first — that is an interactive step for the user.

Decide once at the start: `command -v proton-drive` succeeds **and** the desktop client's folder is
absent means CLI mirror; otherwise desktop client.

If the user passed arguments, treat them as what the next session will focus on and tailor the
document to it.

## 1. Audit unmerged work — and alert

Anything not on `origin/main` is invisible to the other machine unless it is at least pushed. Gather
the evidence:

```bash
git fetch origin --prune
npm run repo:status                                  # public + private companion: dirty, upstream, ahead/behind
git status --porcelain
git stash list
git worktree list
git log --oneline origin/main..HEAD
git branch --no-merged origin/main
git branch -r --no-merged origin/main
gh pr list --state open --author @me --json number,title,headRefName,url
```

This repository squash-merges, so `--no-merged` also lists branches whose work already landed. For
each listed branch, `gh pr list --state merged --head <branch> --json number,headRefOid` — a merged
PR whose `headRefOid` equals the branch tip means the work is on `main`; drop it from the list.
Remote `dependabot/*` branches belong to open Dependabot PRs; group them in one line rather than
itemising.

The audit is complete when every item below is either empty or named:

- uncommitted or stashed changes (public, private companion, each worktree)
- commits not pushed to their upstream
- branches with work not on `origin/main`, pushed or not
- open PRs awaiting merge

**If any item is non-empty, alert the user now**, before writing anything, under a heading
`⚠ Work not merged to main`, with one line per item: repository, branch or path, and state
(uncommitted / unpushed / pushed but unmerged / PR #n open). Unpushed or uncommitted work cannot reach
the other machine — say so explicitly, and ask whether to commit and push it before continuing. Then
continue the handoff either way; the alert informs, it does not block.

## 2. Write the handoff document

**Pull** (CLI mirror only) — refresh the map before reading it, so the entry written on the other
machine is the one this run supersedes:

```bash
mkdir -p "$HANDOFFS_DIR"
proton-drive filesystem download -f remove /my-files/Documents/Handoffs/handoff_map.json "$HANDOFFS_DIR"
```

Resolve the Handoffs folder and this project's current entry:

```bash
node scripts/handoff-map.mjs get
```

It prints `dir`, the map `key` for this repository, and any existing active `file`. If it errors
because no map was found (a new machine, or a different mount point on Fedora), ask the user for the
folder and rerun with `--dir <path>`, suggesting they set `HANDOFFS_DIR` for next time.

Name the file `<repo>-handoff-<YYYY-MM-DD>.md` (repo = the `repo` value printed above). If that name
already exists in `dir`, append a short topic slug: `<repo>-handoff-<YYYY-MM-DD>-<topic>.md`. Write
it straight into `dir`.

Write for a cold reader on the other machine. Sections, in order:

1. **Where you are** — repository, branch, `origin/main` commit, latest release tag, worktree and
   companion state as they will be once pushed. Paths use `<home>` in place of the user directory,
   since the other machine's home differs.
2. **Unmerged work** — the step 1 list, verbatim, or "None — everything is on `origin/main`."
3. **What this session did** — outcomes with their records (PR, issue, commit), not narration.
4. **Next steps** — concrete, ordered, the first one startable without asking.
5. **Open decisions** — anything the user still owes.
6. **Suggested skills** — the skills the next agent should invoke, and at which step.

Reference, never restate, what other artifacts already record — specs, ADRs, issues, PRs, commits,
diffs, the agent memory index — by path or URL. Redact secrets, tokens, passwords, and personal
details.

## 3. Register it in the map

```bash
node scripts/handoff-map.mjs set <file-name>
```

The script refuses a file that is not in the Handoffs folder, and echoes the map entry it read back
after writing. The step is complete when that echo shows `file` equal to `<file-name>` and a fresh
`lastUpdated`. If the project already had a different active handoff, that document is superseded:
leave it in the folder and mention it in the report.

**Push** (CLI mirror only) — the document and the map both leave this machine now:

```bash
proton-drive filesystem upload -f create-new-revision -t \
  "$HANDOFFS_DIR/<file-name>" "$HANDOFFS_DIR/handoff_map.json" /my-files/Documents/Handoffs
```

`create-new-revision` keeps the cloud's earlier map as a revision instead of trashing it. The push is
complete when the transfer summary lists both files as uploaded (an unchanged file reports as
skipped, which is also complete). A summary with neither means the user's login lapsed — see the
transport section above.

## 4. Close the session with end-session

Invoke the **end-session** skill and run it to completion. Tell it the handoff document's path so its
workspace cleanup leaves the Handoffs folder alone — it lives outside the repository and is not
session scratch.

If end-session changes a fact the handoff states — it commits or pushes work, closes or opens issues,
removes a worktree or branch, or the user resolves an open decision — edit the handoff document so it
matches the final state. The document is complete when every "Where you are" and "Unmerged work"
line is true after end-session.
On the CLI mirror, an edited document is pushed again with the same **Push** command; the cloud copy
is what `/lets-go` reads.

## 5. Report

End with:

- the handoff document path and the map entry that now points at it
- the `⚠ Work not merged to main` list as it stands **after** end-session (repeat it even if step 1
  already showed it), or "Nothing unmerged — the other machine can start from `origin/main`."
- end-session's own summary
- the reminder: run `/lets-go` in this repository on the other machine once the document is in the
  cloud folder (the desktop client syncs it within a minute or so; a **Push** puts it there at once)
