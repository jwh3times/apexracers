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

> Run the shell commands through the POSIX shell (Git Bash on Windows). `jq` is not installed;
> the map is read and written only through `node scripts/handoff-map.mjs`.

## 1. Find the active handoff

```bash
node scripts/handoff-map.mjs get
```

Branch on the result:

- **No map found** — ask the user where the Handoffs folder is mounted on this machine, rerun with
  `--dir <path>`, and suggest setting `HANDOFFS_DIR`.
- **`key` is null or `file` is null** — tell the user this repository has no active handoff, name the
  `key` checked, and stop. Leave the map untouched.
- **`exists` is false** — the map names a document Proton Drive has not synced here yet. Tell the
  user the file name and stop without clearing, so a retry after sync still works.
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

## 5. Proceed

Give the user a short brief: where things stand, the unmerged work carried over, the open decisions
still owed, and the first next step. If the user passed arguments, start from what they name;
otherwise start the document's first **Next step**, invoking its **Suggested skills** at the point
the document says. Put an open decision that blocks that step to the user first.
