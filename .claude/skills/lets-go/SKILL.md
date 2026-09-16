---
# GENERATED — DO NOT EDIT. Source: .agents/skills/lets-go/SKILL.md. Regenerate: npm run sync:agents
name: lets-go
description: Resume this repository from its active handoff in the synced Proton Drive handoff_map.json, then clear that entry.
argument-hint: "Anything to focus on first (optional)"
disable-model-invocation: true
---

# Let's go

Pick up work that the handoff skill handed off from the other machine. `handoff_map.json` in the
synced Proton Drive Handoffs folder names each project's **active handoff**; this skill resumes from
this repository's entry and then marks it `null`, so the same handoff is never resumed twice.

**Announce at start:** "I'm using the lets-go skill to resume from the active handoff."

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

## 1. Find the active handoff

**Pull** (CLI mirror only) — fetch the current map before reading it:

```bash
mkdir -p "$HANDOFFS_DIR"
proton-drive filesystem download -f remove /my-files/Documents/Handoffs/handoff_map.json "$HANDOFFS_DIR"
```

```bash
node scripts/handoff-map.mjs get
```

Branch on the result:

- **No map found** — ask the user where the Handoffs folder is mounted on this machine, rerun with
  `--dir <path>`, and suggest setting `HANDOFFS_DIR`.
- **`key` is null or `file` is null** — tell the user this repository has no active handoff, name the
  `key` checked, and stop. Leave the map untouched.
- **`exists` is false**, CLI mirror — the document is fetched by name, then `get` is rerun:

  ```bash
  proton-drive filesystem download -f remove "/my-files/Documents/Handoffs/<file>" "$HANDOFFS_DIR"
  ```

  A `Node not found` reply means the other machine has not synced the document to the cloud yet.
  Tell the user the file name and stop without clearing, so a retry after sync still works.
- **`exists` is false**, desktop client — the map names a document the client has not synced here
  yet. Tell the user the file name and stop without clearing, so a retry after sync still works.
- **`exists` is true** — continue.

## 2. Load it

Read the whole document at `path`. Then follow its pointers that set the ground rules for the work —
the agent memory index it names, and any issue, PR, or spec its **Next steps** start from.

## 3. Bring this machine to the handoff's state

```bash
git fetch origin --prune
npm run repo:status
```

Compare with the document's **Where you are** and **Unmerged work**:

- Behind `origin/main` and clean — `npm run sync:main`.
- The handoff continues on a branch — check it out and fast-forward it from `origin` when the worktree
  is clean; ask first when it is dirty.
- Local state contradicts the document (uncommitted changes here, a commit the document says was
  pushed but `origin` lacks, a branch that no longer exists) — report each contradiction to the user
  and ask how to proceed before step 4.

The step is complete when the checkout matches the document or every mismatch has the user's answer.

## 4. Clear the active handoff

```bash
node scripts/handoff-map.mjs clear
```

Complete when the echoed entry shows `file: null`. The handoff document itself stays in the folder as
a record.

**Push** (CLI mirror only) — the cleared map goes back to the cloud, so the other machine cannot
resume the same handoff a second time:

```bash
proton-drive filesystem upload -f create-new-revision -t "$HANDOFFS_DIR/handoff_map.json" /my-files/Documents/Handoffs
```

Complete when the transfer summary lists the map as uploaded.

## 5. Proceed

Give the user a short brief: where things stand, the unmerged work carried over, the open decisions
still owed, and the first next step. If the user passed arguments, start from what they name;
otherwise start the document's first **Next step**, invoking its **Suggested skills** at the point
the document says. Put an open decision that blocks that step to the user first.
