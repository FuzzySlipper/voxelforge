#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

echo "=== VoxelForge Bridge Smoke Test (C# only) ==="
dotnet build src/VoxelForge.Bridge/VoxelForge.Bridge.csproj
dotnet test tests/VoxelForge.Bridge.Tests/VoxelForge.Bridge.Tests.csproj --no-build
echo "=== C# bridge smoke test passed ==="
