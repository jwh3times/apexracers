# Human follow-ups from completed agent work

Apply this procedure whenever completed agent work leaves a required human action: account or
mailbox verification, unavailable credentials or access, an operator procedure, an external-client
update, a decision, or a manual acceptance check. The agent completing the work owns this handoff;
docs-updater and the ship/end-session workflows verify it. A final-answer checklist alone is insufficient.

## Destinations

- Create follow-up issues in `jwh3times/apexracers-private`, explicitly selecting that repository
  even when running from the public checkout.
- Add each issue to the existing private ApexRacers board:
  `https://github.com/users/jwh3times/projects/2`. This is the shared board for both repositories;
  do not create another board or put maintainer follow-ups in public issues.
- Publish the step-by-step procedure in the private companion wiki's `human-todo` page, or a focused
  wiki page linked from `human-todo`. Issues own status; the wiki owns execution instructions.

## Procedure

1. Inventory every required human action arising from the completed work. Complete actions the agent
   can already perform within the authorized scope. Record a conditional requirement with its exact
   trigger and applicability; do not present it as currently blocking when the trigger has not occurred.
2. Search existing private issues (including closed ones), board items, and wiki instructions for the
   same action and environment/client. Update a matching open follow-up rather than duplicating it;
   reopen only when the same obligation recurs, otherwise link the historical issue as context.
3. Create or update a private follow-up issue for each independently completable action (a single
   ordered operator procedure may use one issue). Include the originating work/PR or commit, why a
   human is needed, the responsible person or role, affected environment/client, applicability and
   prerequisites, a completion checklist, and the wiki link. Label it `ready-for-human`; if absent in
   the private repository, create that label using `docs/agents/triage-labels.md`'s meaning.
4. Add the issue to board 2 with `gh project item-add 2 --owner jwh3times --url <private-issue-url>`.
   Set Status and Blocked by from current evidence: Todo when actionable, Blocked with its prerequisite
   when unavailable, or Parked for a conditional action whose trigger has not occurred. Discover field
   and option IDs instead of assuming them. Record new blocker text in the issue and ensure the board
   reflects it. An unmerged prerequisite remains pending; creating a follow-up does not mark the parent
   work deployed or the human action complete.
5. Publish numbered wiki instructions with prerequisites/access, exact ordered commands or UI actions,
   expected results, verification and completion criteria, and recovery/rollback guidance where relevant.
   Link back to the private issue and source work, and index focused pages from `human-todo`. Existing
   runbooks may supply detail, but the wiki must give a usable sequence rather than just a bare link.
   Verify commands/UI steps from available evidence; identify missing information instead of inventing it.
   A wizard supplements these durable instructions and does not replace them.
6. Verify the issue, label, board membership/status, published wiki page, and reciprocal links. Report
   both issue and wiki URLs in the handoff. Close the issue and mark the board Done only after evidence
   the human action was performed (or an explicit decision it is not applicable), recording that outcome
   in the wiki without erasing reusable instructions.

Use explicit `--repo jwh3times/apexracers-private` for issue commands. For multiline bodies, write a
local temporary file and use `--body-file`. Publish wiki edits through its separate Git remote
`https://github.com/jwh3times/apexracers-private.wiki.git`, preserving other pages and concurrent edits.
This wiki publication is distinct from pushing source-code or companion-worktree branches.

Keep resolved secrets, tokens, personal account identifiers, and credential values out of issues and
wiki pages; use secret-store references or placeholders. Security findings remain in private draft
advisories; the follow-up issue tracks the human remediation action and links to the advisory.

The local companion checkout is optional: its absence does not prevent remote issue/wiki updates
when authorized access exists. If private-repository, board, or wiki access is unavailable, complete
all accessible parts, report exactly what remains unpublished and why, and provide a sanitized draft.
Do not substitute a public issue or claim the handoff complete. If no required human action remains,
report that explicitly and create no empty follow-up.
