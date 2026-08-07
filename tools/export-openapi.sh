#!/usr/bin/env bash

set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
api_project="$repository_root/backend/Unload.Api/Unload.Api.csproj"
schema_directory="$repository_root/openapi"
schema_path="$schema_directory/Unload.Api.json"
listen_url="http://127.0.0.1:5099"
temporary_schema="$(mktemp)"
api_log="$(mktemp)"
api_pid=""

cleanup() {
    if [[ -n "$api_pid" ]] && kill -0 "$api_pid" 2>/dev/null; then
        kill "$api_pid" 2>/dev/null || true
        wait "$api_pid" 2>/dev/null || true
    fi

    rm -f "$temporary_schema" "$api_log"
}

trap cleanup EXIT

if curl --silent --output /dev/null "$listen_url" >/dev/null 2>&1; then
    echo "Port 5099 is already in use; OpenAPI export was not started." >&2
    exit 1
fi

dotnet build "$api_project" --nologo

OpenApiGenerationOnly=true \
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS="$listen_url" \
dotnet run --project "$api_project" --no-build --no-launch-profile >"$api_log" 2>&1 &
api_pid="$!"

for _ in {1..30}; do
    if curl --silent --fail "$listen_url/openapi/v1.json" --output "$temporary_schema"; then
        mkdir -p "$schema_directory"
        mv "$temporary_schema" "$schema_path"
        temporary_schema=""
        echo "OpenAPI schema exported to $schema_path"
        exit 0
    fi

    if ! kill -0 "$api_pid" 2>/dev/null; then
        cat "$api_log" >&2
        exit 1
    fi

    sleep 1
done

cat "$api_log" >&2
echo "Timed out waiting for the OpenAPI endpoint." >&2
exit 1
