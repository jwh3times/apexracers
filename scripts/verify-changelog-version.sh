#!/usr/bin/env bash
#
# Verifies that the top dated CHANGELOG section names the version the next merge
# to main will mint. Run by the "Changelog Version" CI check on every PR, and by
# the /ship skill locally before pushing — both call this one script.
#
# Rule:
#   - predicted = scripts/next-version.sh   (the version this merge will mint)
#   - top       = the first "## [x.y.z]" section below "## [Unreleased]"
#   - If tag v<top> already exists, this branch did not add a new dated section
#     (e.g. a dependabot / docs-only / no-ship PR that only touches [Unreleased]).
#     There is nothing to verify — pass.
#   - Otherwise this branch introduced a new dated section, so it MUST equal
#     predicted. If it does not, the prediction drifted (another branch merged
#     first) — re-run /ship to renumber, or the section was written for the wrong
#     release line.
#
# Exit 0 on pass; non-zero with a ::error:: message on failure.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# next-version.sh fetches tags, so they are present for the rev-parse below.
predicted="$(bash "$repo_root/scripts/next-version.sh")"

top="$(
  sed -nE 's/^## \[([0-9]+\.[0-9]+\.[0-9]+)\].*/\1/p' "$repo_root/CHANGELOG.md" | head -1
)"

if [ -z "$top" ]; then
  echo "::error::No dated '## [x.y.z]' section found in CHANGELOG.md."
  exit 1
fi

if git -C "$repo_root" rev-parse -q --verify "refs/tags/v${top}" >/dev/null 2>&1; then
  echo "Top CHANGELOG section [${top}] is already released (tag v${top} exists); no new section to verify. OK."
  exit 0
fi

if [ "$top" = "$predicted" ]; then
  echo "Top CHANGELOG section [${top}] matches the version this merge will mint. OK."
  exit 0
fi

echo "::error::CHANGELOG top section [${top}] does not match the version this merge will mint (${predicted})."
echo "Re-run /ship to renumber the section to ${predicted}, or bump web/package.json if you intend a new release line."
exit 1
