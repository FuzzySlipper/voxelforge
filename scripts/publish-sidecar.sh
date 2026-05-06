#!/usr/bin/env bash
# publish-sidecar.sh — Publish VoxelForge.Bridge sidecar for Electron packaging
#
# Usage:
#   ./scripts/publish-sidecar.sh                         # publish in Release mode
#   ./scripts/publish-sidecar.sh --configuration Debug    # publish in Debug mode
#   ./scripts/publish-sidecar.sh --help                   # show this help
#
# Output: self-contained .NET publish at electron/sidecar/VoxelForge.Bridge
# This directory is consumed by electron-builder via extraResources.
set -euo pipefail

cd "$(dirname "$0")/.."

show_help() {
  sed -n '2,10p' "$0"
  exit 0
}

CONFIGURATION="Release"
RID="linux-x64"

for arg in "$@"; do
  case "$arg" in
    --help) show_help ;;
    --configuration=*) CONFIGURATION="${arg#*=}" ;;
    --configuration) echo "Use --configuration=<value>" && exit 1 ;;
    -c) echo "Use --configuration=<value>" && exit 1 ;;
    --runtime=*) RID="${arg#*=}" ;;
    *) echo "Unknown argument: $arg" && exit 1 ;;
  esac
done

echo "=== Publishing VoxelForge.Bridge sidecar ==="
echo "Configuration: ${CONFIGURATION}"
echo "Runtime:       ${RID}"
echo "Output:        electron/sidecar/"

dotnet publish src/VoxelForge.Bridge/VoxelForge.Bridge.csproj \
  --configuration "${CONFIGURATION}" \
  --runtime "${RID}" \
  --self-contained true \
  --output electron/sidecar \
  --nologo

echo "=== Sidecar published successfully ==="
ls -lh electron/sidecar/VoxelForge.Bridge 2>/dev/null || ls -lh electron/sidecar/ | head -5
