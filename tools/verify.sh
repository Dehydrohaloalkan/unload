#!/usr/bin/env bash

set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
frontend_dir="$repo_root/web/webApp"

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export NUGET_XMLDOC_MODE=skip

require_command() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "Required command is not available: $1" >&2
    exit 127
  fi
}

run_step() {
  local label="$1"
  shift
  echo "[$label]"
  "$@"
}

require_command dotnet
require_command node
require_command npm

cd "$repo_root"
run_step "backend restore" dotnet restore unload.slnx
run_step "backend format and analyzers" \
  dotnet format unload.slnx --verify-no-changes --no-restore --verbosity minimal
run_step "backend build" dotnet build unload.slnx --no-restore
run_step "backend tests" dotnet test unload.slnx --no-build --no-restore

cd "$frontend_dir"
run_step "frontend dependencies" npm ci
run_step "frontend tests and API contract" npm test -- --watch=false
run_step "frontend build" npm run build

echo "Verification passed."
