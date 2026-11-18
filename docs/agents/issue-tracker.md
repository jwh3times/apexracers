# Issue tracker: GitHub

Issues and specs for this repo live as GitHub issues. Use the `gh` CLI for all operations.

## Conventions

- **Create an issue**: `gh issue create --title "..." --body "..."`. Use a heredoc for multi-line bodies.
- **Read an issue**: `gh issue view <number> --json number,title,body,labels,comments`.
- **List issues**: `gh issue list --state open --json number,title,body,labels,comments --jq '[.[] | {number, title, body, labels: [.labels[].name], comments: [.comments[].body]}]'` with appropriate `--label` and `--state` filters.
- **Make an issue a sub-issue of a parent**: `gh issue create --parent <parent> ...`, or `gh issue edit <parent> --add-sub-issue <child>` afterwards (`gh` 2.94+). Older `gh`: `gh api --method POST repos/<owner>/<repo>/issues/<parent>/sub_issues -F sub_issue_id=<child-db-id>` (database id, as in **Blocking** below). Without sub-issues, put `Part of #<parent>` at the top of the child body.
- **Blocking**: GitHub's **native issue dependencies**, the canonical, UI-visible representation. Add an edge with `gh issue create --blocked-by <n>,<n> ...` or `gh issue edit <child> --add-blocked-by <blocker>` (`gh` 2.94+). Older `gh`: `gh api --method POST repos/<owner>/<repo>/issues/<child>/dependencies/blocked_by -F issue_id=<blocker-db-id>`, where `<blocker-db-id>` is the blocker's numeric **database id** (`gh api repos/<owner>/<repo>/issues/<n> --jq .id`, _not_ the `#number` or `node_id`). Where dependencies aren't available, fall back to a `Blocked by: #<n>, #<n>` line at the top of the child body. A ticket is unblocked when every blocker is closed.
- **Count open blockers**: `gh api repos/<owner>/<repo>/issues/<n> --jq .issue_dependencies_summary.blocked_by` (open blockers only; 0 means startable). `gh issue view <n> --json blockedBy` also lists closed blockers, so filter it: `--jq '[.blockedBy.nodes[] | select(.state == "OPEN")] | length'`.
- **Comment on an issue**: `gh issue comment <number> --body "..."`
- **Apply / remove labels**: `gh issue edit <number> --add-label "..."` / `--remove-label "..."`
- **Close**: post the explanation with `gh issue comment <number> --body "..."`, then `gh issue close <number>`. On an already-closed issue, `gh issue close --comment` drops the comment and still exits 0.

Human follow-ups from completed agent work use the private companion issue tracker, shared private
board, and private wiki under `docs/agents/human-follow-up.md`. Always select that repository explicitly
for these follow-ups; public implementation issues retain the conventions above.

Infer the repo from `git remote -v`; `gh` does this automatically when run inside a clone.

File issues only into the user's own repo. Never file into `mattpocock/skills` (the repo these skills come from) unless the user owns it. In a fork, `gh` can default to the upstream parent, so before the first `gh issue create` of a session, check the target with `gh repo view --json nameWithOwner` and confirm it with the user if it is not their repo.

## Pull requests as a triage surface

**PRs as a request surface: no.** _(Set to `yes` if this repo treats external PRs as feature requests; `/triage` reads this flag.)_

When set to `yes`, PRs run through the same labels and states as issues, using the `gh pr` equivalents:

- **Read a PR**: `gh pr view <number>`, then `gh pr view <number> --comments` (off a TTY, `--comments` prints only the comments, not the body), and `gh pr diff <number>` for the diff.
- **List external PRs for triage**: `gh api --paginate 'repos/{owner}/{repo}/pulls?state=open' --jq '.[] | select(.author_association | IN("OWNER","MEMBER","COLLABORATOR") | not) | {number, title, author: .user.login, author_association, labels: [.labels[].name]}'`.
- **Comment / label / close**: `gh pr comment`, `gh pr edit --add-label`/`--remove-label`, `gh pr close`.

GitHub shares one number space across issues and PRs, so a bare `#42` may be either: resolve with `gh pr view 42` and fall back to `gh issue view 42`.

## When a skill says "publish to the issue tracker"

Create a GitHub issue.

## When a skill says "fetch the relevant ticket"


Read it as in **Read an issue** above.
Run `gh issue view <number> --comments`.

## Wayfinding operations

Used by `/wayfinder`. The **map** is a single issue with **child** issues as tickets.

- **Map**: a single issue labelled `wayfinder:map`, holding the Notes / Decisions-so-far / Fog body. `gh issue create --label wayfinder:map`.
- **Child ticket**: an issue linked to the map as a GitHub sub-issue (see **Make an issue a sub-issue of a parent**). Where sub-issues aren't enabled, add the child to a task list in the map body and put `Part of #<map>` at the top of the child body. Labels: `wayfinder:<type>` (`research`/`prototype`/`grilling`/`task`). Once claimed, the ticket is assigned to the driving dev.
- **Blocking**: see **Blocking** under Conventions.
- **Frontier query**: list the map's open children (`gh issue list --state open`, scoped to the map's sub-issues / task list), drop any with an open blocker (see **Count open blockers**, or an open issue in the `Blocked by` line) or an assignee; first in map order wins.
- **Claim**: `gh issue edit <n> --add-assignee @me`, the session's first write.
- **Resolve**: `gh issue comment <n> --body "<answer>"`, then `gh issue close <n>`, then append a context pointer (gist + link) to the map's Decisions-so-far.

## Public repository

jwh3times/apexracers

## Board

jwh3times 2

## Board fields

- `Status`: `Todo` / `In Progress` / `Blocked` / `Parked` / `Done`. An issue set to `Blocked` also
  carries the public `blocked` label (see `docs/agents/triage-labels.md`); clearing the blocker
  removes both.
- `Priority`: `P0 - Highest` / `P1 - High` / `P2 - Normal` / `P3 - Low`, set on every open item
  whether or not it is blocked. Open items sit at the top of the board in priority order. Priority
  lives only on the board; there is no priority label.
- `Blocked by`: the blocker outside the project, set with `Blocked`. The standing
  iRacing-credentials blocker has its own value; a new blocker worth tracking gets its own issue. A
  blocker that is an issue is a native `blocked_by` edge (see **Blocking**), never a value here.

## Priority order

- `Priority`: `P0 - Highest` / `P1 - High` / `P2 - Normal` / `P3 - Low`.
- `board order`: the board's manual order, top first, which follows `Priority`.

## Board mechanics

The board is a user-level project, so every `gh project` call needs `--owner jwh3times`. Field and
option IDs are in the private companion; look them up with
`gh project field-list 2 --owner jwh3times --format json` rather than hardcoding them.

- **Auto-add covers the public repository only, and not reliably.** New public issues usually land at
  `Todo` by themselves; private companion issues never do. Check `item-list` before `item-add`, and
  set any non-default `Status` or `Blocked by` by hand.
- **`item-list` lags writes.** A fresh item can be missing for a minute after a successful `item-add`
  or `item-edit`. Verify a new item by its id with a GraphQL `node(id:)` query instead.
- **`item-list` returns custom fields under their display name, lowercased with spaces kept.**
  `Blocked by` reads back as `item["blocked by"]`, while built-in fields are camelCase (`status`). A
  wrong accessor makes every successful write look failed. Dump one whole item before trusting an
  accessor.
- **For bulk edits, skip `gh project item-edit`.** About 18 in a row hit GitHub's secondary (burst)
  rate limit while the GraphQL budget was nearly full. Use one `gh api graphql` mutation per item with
  aliased `updateProjectV2ItemFieldValue` calls, and pause about 1.5 s between items.
- **Editing a single-select field's options replaces the whole list.** `updateProjectV2Field` keeps
  existing values only if every surviving option is passed with its existing `id`. Without the ids,
  the options are recreated and the field clears on every item that used them. Build the mutation
  from a live read, snapshot every item's value first, and diff afterwards.
- **Converting a draft item to an issue:** use `convertProjectV2DraftIssueItemToIssue` with `itemId`
  and `repositoryId` (it rejects `projectId`). The item id and its fields survive; labels don't.
- **Closing automation is partial.** A PR merge with a closing keyword closes the issue and moves its
  item to Done. An issue closed by hand, or closed before the automation saw it, keeps its old status
  indefinitely. When closing anything by hand, set its Status in the same step. At close-out, list
  every non-Done item and check each one's issue or advisory state directly.
- **Blocking across repositories:** `--add-blocked-by` resolves the blocker in the same repository,
  so a private follow-up blocked by a public issue needs the REST form from **Blocking** above,
  posted to the private repository with the public issue's database id. The edge clears itself when
  the blocker closes, but the `blocked` label and board fields don't.
