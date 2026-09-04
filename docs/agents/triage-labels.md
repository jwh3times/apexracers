# Triage Labels

The skills speak in terms of five canonical triage roles. All five exist as real labels in this
repository, so the mapping is one-to-one and a skill can apply a role name directly.

| Role              | Label in our tracker | Applies when                                                    |
| ----------------- | -------------------- | --------------------------------------------------------------- |
| `needs-triage`    | `needs-triage`       | Filed but not yet evaluated by a maintainer                     |
| `needs-info`      | `needs-info`         | Blocked awaiting information or evidence not yet available      |
| `ready-for-agent` | `ready-for-agent`    | Fully specified; an agent can implement it unattended           |
| `ready-for-human` | `ready-for-human`    | Requires human implementation, access, or judgement             |
| `wontfix`         | `wontfix`            | Will not be actioned                                            |

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

## Changing this vocabulary

The label strings and this table must stay in step, because a skill that applies a documented label
that does not exist fails at the tracker boundary rather than degrading — that is what issue #266
recorded. If you rename or retire a label, run `gh label list` and update this table in the same
change, then confirm the result applies:

```bash
gh issue edit <n> --add-label <label>
```
