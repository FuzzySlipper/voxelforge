#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

echo "=== VoxelForge Electron Smoke Test ==="

# Ensure C# sidecar is built first.
dotnet build src/VoxelForge.Bridge/VoxelForge.Bridge.csproj

# Ensure Electron dependencies are installed.
cd electron
if [ ! -d "node_modules" ]; then
  echo "Installing Electron dependencies..."
  npm install
fi

npm run smoke-test
echo "=== Electron smoke test passed ==="
