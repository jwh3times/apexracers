# Releases

## Scheme

stamp-on-tag — `.github/workflows/version.yml` tags every merge to `main`
`v<major>.<minor>.<build>` with a GitHub Release. Each branch's entries live in its own
`changelog.d/<branch-slug>.md` fragment; the workflow stamps them into the merge's dated section
and tags the merge commit (ADR 0008).

## Version file

`web/package.json` (with `web/package-lock.json` beside it)

## Ask before bumping

when ambiguous
