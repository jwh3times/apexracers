### Changed

- **The changelog moves from `per-merge` to `stamp-on-tag`** (ADR 0008). A branch no longer
  predicts the version its merge will mint: `/ship` writes its entries to its own
  `changelog.d/<branch-slug>.md` fragment and never edits `CHANGELOG.md`. After each merge to
  `main`, `Version` runs the new `scripts/stamp-changelog.mjs`, which turns every untagged merge's
  fragments (or, with none, its PR title) into a dated section with its compare link, pushes one
  `chore(release): v<x.y.z> [skip ci]` commit through the release GitHub App the main ruleset lets
  bypass it, tags each merge commit, and creates its GitHub Release from that section. Open PRs now
  merge in any order without renumbering, and Dependabot merges get their own section instead of
  rolling into the next one. No product behavior changes.
- **The required `Verify changelog version` check keeps its name** but now verifies the branch's
  fragments, and fails a branch that edits a released section, adds a `## [Unreleased]` entry or
  changes a fragment already on `main`. Dependabot PRs pass it without the old author exemption.
- The shared skills are reinstalled from `jwh3times/skills` with `stamp-on-tag` support, and the
  canonical scripts the repository carries are refreshed.
