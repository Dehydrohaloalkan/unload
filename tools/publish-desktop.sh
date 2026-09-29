#!/usr/bin/env bash

set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="$repository_root/backend/Unload.Api/Unload.Api.csproj"

for runtime in linux-x64 win-x64; do
    dotnet publish "$project" \
        --configuration Release \
        --runtime "$runtime" \
        --self-contained true \
        -p:DesktopBuild=true \
        -p:PublishSingleFile=false \
        --output "$repository_root/artifacts/desktop/$runtime"
done

echo "Desktop builds: $repository_root/artifacts/desktop"
