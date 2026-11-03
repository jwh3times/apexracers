# Triage labels

The labels and what they mean are in `.agents/skills/ship/settings/triage-labels.md`. A skill's role name is the GitHub label to apply.

## How this repository uses them

`needs-info` covers **any** missing evidence, not only an unanswered question to a reporter. Issues
#193 and #230 carry it because each needs a real captured payload before it can be designed — a
public label is the one signal a community member could act on to unblock them.

`ready-for-human` is not a judgement about difficulty. It means an agent cannot complete the work
because it needs credentials, cloud access, or a decision only the maintainer can make — the M2
operator issues are the standing example.

## Labels versus the project board

These labels are **public** and live on the issue. The [project board][board] is **private** and
carries planning state — `Status` (`Todo` / `In Progress` / `Blocked` / `Parked` / `Done`) and
`Blocked by`.

Do not mirror one into the other. A label says something an outside contributor should be able to
act on; a board field says where the work sits in the maintainer's queue. The overlap is deliberate
and small: an issue can be `Blocked` on the board with no label at all when the blocker is purely
internal.

[board]: https://github.com/users/jwh3times/projects/2
