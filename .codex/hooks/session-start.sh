#!/bin/bash
set -euo pipefail

# Bootstrap the .NET 10 SDK on a fresh Linux agent sandbox (Claude Code web, Codex
# cloud, or any apt-based CI box). Gated on capability rather than on one tool's
# environment variable, so the same script is correct for every agent runner — Codex
# exposes no cloud/remote indicator to key off, and this needs none.
#
# Skip where an apt install cannot or should not run:
#   - no apt-get (a local Windows/macOS dev box) — nothing to do
if ! command -v apt-get &>/dev/null; then
  exit 0
fi

# Skip if the .NET 10 SDK is already on the PATH
if command -v dotnet &>/dev/null && dotnet --version 2>/dev/null | grep -q "^10\."; then
  echo "dotnet $(dotnet --version) already available — skipping install"
  exit 0
fi

echo "Installing .NET 10 SDK via apt..."
apt-get update -qq
apt-get install -y dotnet-sdk-10.0

echo "dotnet installed: $(dotnet --version)"
