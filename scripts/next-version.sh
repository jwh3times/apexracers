#!/usr/bin/env bash
#
# Prints the bare SemVer version (e.g. "0.4.14", no "v" prefix) that the NEXT
# merge to main will mint. This is the single source of truth the /ship skill
# uses to date the CHANGELOG entry.
#
# It MIRRORS the algorithm in .github/workflows/version.yml — keep the two in
# sync. If you change how releases are numbered there, change it here too.
#
# Algorithm (identical to version.yml):
#   - The release line is web/package.json "version" (x.y.z); x.y selects the line.
#   - Among existing three-part tags vX.Y.N, the next build is max(N) + 1.
#   - If no vX.Y.N tag exists yet, the declared z is accepted as the first build
#     (so vX.Y.0 is valid for a fresh major/minor bump).
#   - Legacy four-part tags such as v0.3.0.6 are ignored.
#
# Note: this predicts based on tags visible right now. If another branch merges
# to main before yours does, it consumes the build number this printed — the
# /ship skill treats the CHANGELOG section it writes as re-numberable for that
# reason, and the "Changelog Version" CI check (scripts/verify-changelog-version.sh)
# flags such drift on the PR.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

declared="$(
  sed -nE 's/^[[:space:]]*"version"[[:space:]]*:[[:space:]]*"([0-9]+\.[0-9]+\.[0-9]+)".*/\1/p' \
    "$repo_root/web/package.json" | head -1
)"

if [ -z "$declared" ]; then
  echo "Could not read a plain x.y.z \"version\" from web/package.json." >&2
  exit 1
fi

major="${declared%%.*}"
rest="${declared#*.}"
minor="${rest%%.*}"
declared_build="${declared##*.}"

# Best-effort refresh so the prediction reflects the latest remote tags.
# Ignore failures (offline / no network) — fall back to whatever is local.
git -C "$repo_root" fetch --tags -q origin 2>/dev/null || true

last="$(
  git -C "$repo_root" tag --list "v${major}.${minor}.*" \
    | sed -E "s/^v${major}\.${minor}\.//" \
    | grep -E '^[0-9]+$' \
    | sort -n \
    | tail -1 || true
)"

if [ -z "$last" ]; then
  build="$declared_build"
else
  build=$(( last + 1 ))
fi

echo "${major}.${minor}.${build}"
