# Releases

## Scheme

per-merge

## Skipped tags

roll up — merges that wrote no section (dependency bumps, mostly) are tracked cumulatively, and no
section is invented per skipped build tag.

## Version file

`web/package.json` (with `web/package-lock.json` beside it)

## Bump command

`npm version <version> --no-git-tag-version`, run from `web/`

## Ask before bumping

when ambiguous

## Next-version command

`bash scripts/next-version.sh`

## Unreleased placeholder

`No unreleased changes.`

## Changelog check

`bash scripts/verify-changelog-version.sh` (the required **Verify changelog version** check)

## Release commit message

`chore(release): prepare v<version>`
