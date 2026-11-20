# Stamp each merge's changelog section when it is tagged, not when it ships

Status: **accepted by the maintainer on 2026-11-19** (#543).

Every merge to `main` is a release, and `CHANGELOG.md` gives each one a dated section. A branch no
longer names the version its merge will mint: `ship` writes the branch's entries to its own
`changelog.d/<branch-slug>.md` fragment and never edits `CHANGELOG.md`. After the merge,
`.github/workflows/version.yml` runs `scripts/stamp-changelog.mjs`, which turns each untagged
merge's fragments (or, with none, its PR title) into a dated section with its compare link,
commits the sections with `[skip ci]`, and tags the merge commit. The version is still
`scripts/next-version.mjs`'s computation from the `web/package.json` line, now made once, at tag
time, over the merges in the order they landed.

Until this record, the rule was `per-merge` and no ADR held it. A PR predicted its version at ship
time and wrote that section, so every merge invalidated every open PR's section: whichever of two
open PRs merged second had to renumber its heading and footer links, and `Verify changelog
version` failed it until it did. Dependabot PRs were exempt from the check, so their builds shipped
with no section and the next human PR rolled their changes up (`Skipped tags: roll up`), which is
why some tags before this record have no section. Fragments cannot collide, and a merge that
wrote none still gets a section, so none of that survives.

The stamp commit goes straight to `main`, which the ruleset otherwise forbids, so `version.yml`
pushes it with a token from a release GitHub App listed as the ruleset's one bypass actor
(`GITHUB_TOKEN` cannot be one). Every first-parent commit on `main` is a release, so the ruleset
allows only merge commits. `[skip ci]` keeps the stamp commit from starting any push workflow, so
`Build and Deploy` deploys each merge commit and never the stamp commit, which changes only
`CHANGELOG.md` and `changelog.d/`. The stamp commit does put every open PR behind `main` again,
and the ruleset is strict about up-to-date branches, so updating a PR waits for the `Version` run
(`docs/agents/merging.md`). The `Verify changelog version` check keeps its name, which the ruleset
requires, and now checks the fragments instead of a predicted version, with no Dependabot
exemption. The scheme and its operating rules are `stamp-on-tag` in `jwh3times/skills`, which
holds the canonical scripts.
