# Triage labels

The labels and what they mean are in `.agents/skills/ship/settings/triage-labels.md`. A skill's role name is the GitHub label to apply.

## How this repository uses them

`needs-info` covers **any** missing evidence, not only an unanswered question to a reporter. Issues
#193 and #230 carry it because each needs a real captured payload before it can be designed — a
public label is the one signal a community member could act on to unblock them.

`ready-for-human` is not a judgement about difficulty. It means an agent cannot complete the work
because it needs credentials, cloud access, or a decision only the maintainer can make — the M2
operator issues are the standing example. When the human part of an issue is tracked by its own
follow-up (a capture, a registration, a resource selection), that follow-up carries
`ready-for-human` and the implementation issue does not: the implementation issue says what it
waits for with `needs-info` and `blocked`.

## Extra labels

- `blocked` — the issue is waiting on another issue or an external event and cannot be progressed
  now, so nobody should pick it up. It sits beside the five rather than replacing one: the triage
  label still says what the issue will need once unblocked (`needs-info`, `ready-for-human`, …).
  Apply it with the board's `Blocked` status and remove it in the same change that clears the
  blocker. It is not applied to `Parked` work, which waits on a decision to schedule it rather than
  on a prerequisite.

## Labels versus the project board

These labels are **public** and live on the issue. The [project board][board] is **private** and
carries planning state — `Status` (`Todo` / `In Progress` / `Blocked` / `Parked` / `Done`),
`Priority` (`P0`–`P3`) and `Blocked by`.

A label says something an outside contributor should be able to act on; a board field says where the
work sits in the maintainer's queue. `blocked` is the one deliberate mirror: an issue `Blocked` on
the board carries the `blocked` label too, so the public issue says it is not available to pick up.
What it waits on stays where it already lives: a native `blocked_by` edge when the blocker is an
issue, otherwise the board's `Blocked by` field. Priority stays on the board only.

[board]: https://github.com/users/jwh3times/projects/2
