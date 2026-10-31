# Human follow-up

Required human actions left by completed agent work follow the procedure in the installed skills'
`human-follow-up` settings definition (for example
`.agents/skills/end-session/settings/human-follow-up.md`). The agent that completes the work owns
the follow-up; docs-updater, ship and end-session verify it.

## Follow-up board

same as Board — `Status` `Todo` when actionable, `Blocked` with `Blocked by` naming the prerequisite,
or `Parked` for a conditional action whose trigger has not occurred. An unmerged prerequisite stays
pending: a follow-up never marks the parent work deployed.
