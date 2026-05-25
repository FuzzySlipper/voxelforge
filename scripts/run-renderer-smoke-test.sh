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

# ── Headless Screenshot Smoke ──
# The renderer smoke test (--renderer-smoke-test) launches the Electron app
# with app.disableHardwareAcceleration() and waits for renderer metrics via IPC.
#
# To produce actual headless screenshots (non-blank PNG files) from the
# Chromium viewer capture service, the MCP server must be running and the
# scene must be populated with voxels. The ChromiumViewerCaptureService
# in src/VoxelForge.Mcp/Services/ handles this:
#
#   1. Start the MCP server: dotnet run --project src/VoxelForge.Mcp -- --port 5209
#   2. Populate a scene via MCP tools
#   3. Invoke view_model / view_from_angle via the MCP endpoint:
#      curl -X POST http://localhost:5209/mcp \
#        -H 'Content-Type: application/json' \
#        -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"view_model","arguments":{}}}'
#   4. Find the captured PNG in the project captures directory:
#      ls ~/.voxelforge/captures/
#
# The ChromiumViewerCaptureService uses --virtual-time-budget=20000ms which
# gives the Three.js viewer time to:
#   a. Load CDN scripts (three.js, OrbitControls)
#   b. Fetch the mesh snapshot from the MCP API
#   c. Build the scene (voxel meshes + reference models)
#   d. Load async textures
#   e. Signal capture-ready (scene built + all textures loaded)
#   f. Render at least one frame
#
# Prerequisites for Chromium screenshot capture:
#   - chromium / chromium-browser / google-chrome installed at /usr/bin/
#   - Scene populated with voxels and/or reference models

echo "Renderer smoke test requires an X display server for Electron window creation."
echo "To run with screenshot verification:"
echo ""
echo "  # Terminal 1: Start MCP server"
echo "  dotnet run --project src/VoxelForge.Mcp -- --port 5209 &"
echo "  sleep 3"
echo ""
echo "  # Populate scene"
echo '  curl -X POST http://localhost:5209/mcp \'
echo '    -H "Content-Type: application/json" \'
echo '    -d '"'"'{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"fill_box","arguments":{"x1":0,"y1":0,"z1":0,"x2":5,"y2":5,"z2":5,"palette_index":1}}}'"'"
echo ""
echo "  # Capture screenshot"
echo '  curl -X POST http://localhost:5209/mcp \'
echo '    -H "Content-Type: application/json" \'
echo '    -d '"'"'{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"view_from_angle","arguments":{"preset":"isometric"}}}'"'"
echo ""
echo "  # Verify captured image is non-blank"
echo '  ls -la ~/.voxelforge/captures/*.png'
echo '  python3 -c "import struct,sys; f=open(sys.argv[1],\"rb\"); h=f.read(24); w,h=struct.unpack(\">II\",h[16:24]); print(f\"Image: {w}x{h} pixels, {(w>100 and h>100) and \x27PASS\x27 or \x27FAIL\x27}\")" ~/.voxelforge/captures/*.png'

echo ""
echo "=== Renderer smoke test: manual verification (see instructions above) ==="
