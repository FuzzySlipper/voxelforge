#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

echo "=== VoxelForge Renderer Smoke Test ==="

# Ensure C# sidecar is built
dotnet build src/VoxelForge.Bridge/VoxelForge.Bridge.csproj

# Ensure Electron dependencies are installed
cd electron
if [ ! -d "node_modules" ]; then
  echo "Installing Electron dependencies..."
  npm install
fi

npm run build

# Copy renderer HTML
mkdir -p dist/renderer
cp src/renderer/renderer.html dist/renderer/renderer.html

# Run headless renderer smoke test (exits after metrics collection)
npm run renderer-smoke-test
echo "=== Renderer smoke test passed ==="