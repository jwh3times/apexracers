#!/bin/bash
set -euo pipefail

# Only install in remote Claude Code on the web environments
if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

# Skip if .NET 10 SDK is already on the PATH
if command -v dotnet &>/dev/null && dotnet --version 2>/dev/null | grep -q "^10\."; then
  echo "dotnet $(dotnet --version) already available — skipping install"
  exit 0
fi

echo "Installing .NET 10 SDK via apt..."
apt-get update -qq
apt-get install -y dotnet-sdk-10.0

echo "dotnet installed: $(dotnet --version)"
