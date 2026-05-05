#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

echo "=== Building C# sidecar ==="
dotnet build src/VoxelForge.Bridge/VoxelForge.Bridge.csproj

echo "=== Installing Electron dependencies ==="
cd electron
if [ ! -d "node_modules" ]; then
  echo "Installing..."
  npm install
fi

echo "=== Compiling TypeScript ==="
npm run build

echo "=== Copying renderer HTML ==="
mkdir -p dist/renderer
cp src/renderer/renderer.html dist/renderer/renderer.html

echo "=== Build complete ==="