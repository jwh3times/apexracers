# Human follow-up

Required human actions left by completed agent work follow the procedure in the installed skills'
`human-follow-up` settings definition (for example
`.agents/skills/end-session/settings/human-follow-up.md`). The agent that completes the work owns
the follow-up; docs-updater, ship and end-session verify it.

## Follow-up destination

Issues in jwh3times/apexracers-private, selected explicitly with `--repo` even from the public
checkout.

## Follow-up label

`ready-for-human`

## Follow-up board

same as Board — `Status` `Todo` when actionable, `Blocked` with `Blocked by` naming the prerequisite,
or `Parked` for a conditional action whose trigger has not occurred. An unmerged prerequisite stays
pending: a follow-up never marks the parent work deployed.

## Follow-up wiki

The private repository's wiki (`https://github.com/jwh3times/apexracers-private.wiki.git`), index
page `human-todo`; focused pages are linked from it. Push without asking: publishing the
instructions is part of the follow-up.
