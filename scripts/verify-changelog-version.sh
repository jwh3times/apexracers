#!/usr/bin/env bash
#
# Verifies that the top dated CHANGELOG section names the version the next merge
# to main will mint. Run by the "Changelog Version" CI check on every PR, and by
# the /ship skill locally before pushing — both call this one script.
#
# Rule:
#   - predicted = scripts/next-version.sh   (the version this merge will mint)
#   - top       = the first "## [x.y.z]" section below "## [Unreleased]"
#   - If that heading already exists on main, this branch introduces no new dated
#     section — a dependabot / docs-only / no-ship PR that only touches
#     [Unreleased], or one that edits released history in place. Nothing to
#     verify — pass.
#   - Otherwise this branch introduced the section, so it MUST equal predicted.
#     If it does not, the prediction drifted (another branch merged first and
#     consumed that build number) — re-run /ship to renumber.
#
# Why the test is "is this heading new vs main?" and NOT "does tag v<top> exist?":
# a section written for vX that is then overtaken by someone else's merge minting
# vX looks *identical* to already-released history under a tag check. So a tag
# check silently passes exactly the drift this script exists to catch. Asking
# whether the branch introduced the heading separates the two cases cleanly.
#
# Exit 0 on pass; non-zero with a ::error:: message on failure.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# next-version.sh fetches tags and derives the x.y line from web/package.json.
predicted="$(bash "$repo_root/scripts/next-version.sh")"

top="$(
  sed -nE 's/^## \[([0-9]+\.[0-9]+\.[0-9]+)\].*/\1/p' "$repo_root/CHANGELOG.md" | head -1
)"

if [ -z "$top" ]; then
  echo "::error::No dated '## [x.y.z]' section found in CHANGELOG.md."
  exit 1
fi

# Resolve main so we can ask whether the top heading is new on this branch.
# On a PR, actions/checkout builds the test-merge ref, so origin/main is the
# base; locally (/ship) a plain 'main' ref is the normal case.
resolve_base() {
  local ref
  for ref in origin/main main; do
    if git -C "$repo_root" rev-parse -q --verify "${ref}^{commit}" >/dev/null 2>&1; then
      printf '%s' "$ref"
      return 0
    fi
  done
  # Shallow / single-branch clone: fetch main explicitly rather than give up.
  if git -C "$repo_root" fetch -q origin main >/dev/null 2>&1 &&
    git -C "$repo_root" rev-parse -q --verify FETCH_HEAD >/dev/null 2>&1; then
    printf '%s' FETCH_HEAD
    return 0
  fi
  return 1
}

if ! base_ref="$(resolve_base)"; then
  echo "::error::Could not resolve 'main' to compare CHANGELOG.md against, so this"
  echo "check cannot tell whether the branch adds a new dated section. Fetch main"
  echo "(git fetch origin main) and re-run; in CI ensure checkout uses fetch-depth: 0."
  exit 1
fi

base_changelog="$(git -C "$repo_root" show "${base_ref}:CHANGELOG.md" 2>/dev/null || true)"

# Literal-dot match, so 0.4.1 can never match the 0.4.15 heading.
top_pattern="${top//./\\.}"

# Here-string, NOT `printf ... | grep -q`: with -q, grep exits the moment it
# matches and closes the pipe, so printf's remaining writes fail with EPIPE. Under
# `pipefail` that makes the *successful match* look like a failed test, sending
# every exempt PR down the drift-error path. A here-string has no reader to close.
if grep -qE "^## \[${top_pattern}\]" <<<"$base_changelog"; then
  echo "Top CHANGELOG section [${top}] already exists on ${base_ref}; this branch adds no new dated section. OK."
  exit 0
fi

if [ "$top" = "$predicted" ]; then
  echo "Top CHANGELOG section [${top}] matches the version this merge will mint. OK."
  exit 0
fi

echo "::error::CHANGELOG top section [${top}] is new on this branch but does not match"
echo "the version this merge will mint (${predicted}). Another branch likely merged first"
echo "and consumed v${top}. Re-run /ship to renumber the section to ${predicted}, or bump"
echo "web/package.json if you intend to start a new release line."
exit 1
