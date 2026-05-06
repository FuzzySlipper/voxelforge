#!/usr/bin/env bash
# run-electron-dev.sh — VoxelForge Electron dev workflow
#
# Usage:
#   ./scripts/run-electron-dev.sh                  # Build + start Electron (sidecar auto-launched)
#   ./scripts/run-electron-dev.sh --preview <path>  # Build + start Electron, auto-load preview file
#   ./scripts/run-electron-dev.sh --sidecar-only    # Build + run sidecar only (for debugging)
#   ./scripts/run-electron-dev.sh --help            # Show this help
#
# This script handles the full development cycle:
#   1. Build the C# sidecar (VoxelForge.Bridge)
#   2. Install Electron dependencies if missing
#   3. Build TypeScript sources
#   4. Copy renderer HTML to dist/
#   5. Launch Electron (which auto-spawns the sidecar)
#
# For the sidecar-only mode:
#   The sidecar is started independently with console output visible.
#   Useful for inspecting bridge protocol messages during development.
#
# Requirements:
#   - .NET 10 SDK (for sidecar build)
#   - Node.js 20+ and npm (for Electron build)
#   - git submodules initialized (git submodule update --init --recursive)
set -euo pipefail

cd "$(dirname "$0")/.."

show_help() {
  sed -n '2,22p' "$0"
  exit 0
}

# Parse args
PREVIEW_ARGS=()
SIDECAR_ONLY=false
for arg in "$@"; do
  case "$arg" in
    --help) show_help ;;
    --sidecar-only) SIDECAR_ONLY=true ;;
    *) PREVIEW_ARGS+=("$arg") ;;
  esac
done

echo "=== VoxelForge Electron Dev Workflow ==="

# 1. Build C# sidecar
echo "--- Building C# sidecar (VoxelForge.Bridge) ---"
dotnet build src/VoxelForge.Bridge/VoxelForge.Bridge.csproj

if [ "$SIDECAR_ONLY" = true ]; then
  echo "--- Starting sidecar only (stdout shows bridge protocol) ---"
  echo "Press Ctrl+C to stop."
  dotnet run --project src/VoxelForge.Bridge
  exit 0
fi

# 2. Install Electron dependencies if missing
cd electron
if [ ! -d "node_modules" ]; then
  echo "--- Installing Electron dependencies ---"
  npm install
fi

# 3. Build TypeScript
echo "--- Compiling TypeScript ---"
npm run build

# 4. Copy renderer HTML
mkdir -p dist/renderer
cp src/renderer/renderer.html dist/renderer/renderer.html

echo "--- Launching Electron ---"
if [ ${#PREVIEW_ARGS[@]} -gt 0 ]; then
  echo "Args: ${PREVIEW_ARGS[*]}"
  npx electron . "${PREVIEW_ARGS[@]}"
else
  npx electron .
fi
