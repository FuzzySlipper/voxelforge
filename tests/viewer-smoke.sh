#!/usr/bin/env bash
#
# VoxelForge Viewer Smoke Test
#
# Validates the browser viewer API endpoints are serving correct responses.
# This validates the HTTP serving layer — model data population is tested
# in the C# unit tests (ViewerEndpointTests.cs).
#
# For end-to-end visual verification with model data:
#   1. Start the MCP server
#   2. Use a proper MCP client (Claude Code, etc.) to populate the model
#   3. Open http://localhost:5201/viewer in a browser
#
# Usage:
#   bash tests/viewer-smoke.sh [port]
#
# Requires: curl, python3

set -euo pipefail

PORT="${1:-5209}"
BASE="http://localhost:${PORT}"

cleanup() {
  echo "Cleaning up..."
  kill "$SERVER_PID" 2>/dev/null || true
  wait "$SERVER_PID" 2>/dev/null || true
}
trap cleanup EXIT INT TERM

pass=0
fail=0

check() {
  local desc="$1"
  shift
  if "$@"; then
    echo "  ✓ $desc"
    pass=$((pass + 1))
  else
    echo "  ✗ $desc"
    fail=$((fail + 1))
  fi
}

echo "=== VoxelForge Viewer Smoke Test ==="
echo "Port: ${PORT}"
echo ""

# ── Start MCP server ──
echo "Starting MCP server..."
dotnet run --project "$(dirname "$0")/../src/VoxelForge.Mcp" -- \
  --port "$PORT" > /dev/null 2>&1 &
SERVER_PID=$!
sleep 3

# Verify server is running
if ! kill -0 "$SERVER_PID" 2>/dev/null; then
  echo "ERROR: Server failed to start"
  exit 1
fi

# ── 1. Health Check ──
echo "=== 1. Health Check ==="
HEALTH=$(curl -sf "$BASE/health" 2>/dev/null || echo "")
check "Server returns healthy" bash -c "echo '$HEALTH' | python3 -c \"import json,sys; d=json.load(sys.stdin); assert d.get('status')=='healthy'; assert 'viewer_endpoint' in d\""

# ── 2. Root endpoint announces viewer ──
echo ""
echo "=== 2. Root Endpoint ==="
ROOT=$(curl -sf "$BASE/" 2>/dev/null || echo "")
check "Root includes viewer_endpoint" bash -c "echo '$ROOT' | python3 -c \"import json,sys; d=json.load(sys.stdin); assert d.get('viewer_endpoint')=='/viewer'\""

# ── 3. Viewer HTML page ──
echo ""
echo "=== 3. Viewer HTML ==="
HTML=$(curl -sf "$BASE/viewer" 2>/dev/null || echo "")
check "Returns HTML content" bash -c "echo '$HTML' | head -1 | grep -q '<\!DOCTYPE html>'"
check "Includes Three.js CDN" bash -c "echo '$HTML' | grep -q 'cdnjs.cloudflare.com/ajax/libs/three.js'"
check "Includes OrbitControls" bash -c "echo '$HTML' | grep -q 'examples/js/controls/OrbitControls'"
check "Includes diagnostics overlay" bash -c "echo '$HTML' | grep -q 'diag-overlay'"
check "No editing controls" bash -c "echo '$HTML' | grep -qv 'undo-button\|redo-button\|palette-list\|tool-list'"
check "Has polling interval" bash -c "echo '$HTML' | grep -q 'POLL_INTERVAL_MS'"
check "No Electron APIs" bash -c "echo '$HTML' | grep -qv 'electron\|preload\|voxelforgeBridge'"

# ── 4. Viewer State API ──
echo ""
echo "=== 4. State API ==="
STATE=$(curl -sf "$BASE/api/viewer-state" 2>/dev/null || echo "")
check "Returns valid JSON" bash -c "echo '$STATE' | python3 -c \"import json,sys; json.load(sys.stdin)\""
check "Has revision field" bash -c "echo '$STATE' | python3 -c \"import json,sys; assert 'revision' in json.load(sys.stdin)\""
check "Has model_name field" bash -c "echo '$STATE' | python3 -c \"import json,sys; assert 'model_name' in json.load(sys.stdin)\""
check "Has voxel_count field" bash -c "echo '$STATE' | python3 -c \"import json,sys; assert 'voxel_count' in json.load(sys.stdin)\""
check "Has palette_entries field" bash -c "echo '$STATE' | python3 -c \"import json,sys; assert 'palette_entries' in json.load(sys.stdin)\""
check "Model name is 'untitled'" bash -c "echo '$STATE' | python3 -c \"import json,sys; assert json.load(sys.stdin).get('model_name')=='untitled'\""

# ── 5. Mesh Snapshot API ──
echo ""
echo "=== 5. Mesh Snapshot API ==="
MESH=$(curl -sf "$BASE/api/mesh-snapshot" 2>/dev/null || echo "")
check "Returns valid JSON" bash -c "echo '$MESH' | python3 -c \"import json,sys; json.load(sys.stdin)\""
check "Has model_id field" bash -c "echo '$MESH' | python3 -c \"import json,sys; assert 'model_id' in json.load(sys.stdin)\""
check "Has mesh_id field" bash -c "echo '$MESH' | python3 -c \"import json,sys; assert 'mesh_id' in json.load(sys.stdin)\""
check "Has vertex_count field" bash -c "echo '$MESH' | python3 -c \"import json,sys; assert 'vertex_count' in json.load(sys.stdin)\""
check "Has triangle_count field" bash -c "echo '$MESH' | python3 -c \"import json,sys; assert 'triangle_count' in json.load(sys.stdin)\""
check "Has positions array" bash -c "echo '$MESH' | python3 -c \"import json,sys; assert 'positions' in json.load(sys.stdin)\""
check "Has normals array" bash -c "echo '$MESH' | python3 -c \"import json,sys; assert 'normals' in json.load(sys.stdin)\""
check "Has colors array" bash -c "echo '$MESH' | python3 -c \"import json,sys; assert 'colors' in json.load(sys.stdin)\""
check "Has indices array" bash -c "echo '$MESH' | python3 -c \"import json,sys; assert 'indices' in json.load(sys.stdin)\""
check "Empty model has 0 vertices" bash -c "echo '$MESH' | python3 -c \"import json,sys; assert json.load(sys.stdin).get('vertex_count')==0\""
check "Serialized with snake_case naming" bash -c "echo '$MESH' | python3 -c \"import json,sys; d=json.load(sys.stdin); assert 'vertex_count' in d and 'model_id' in d and 'mesh_id' in d and 'triangle_count' in d\""

# ── 6. Palette API ──
echo ""
echo "=== 6. Palette API ==="
PALETTE=$(curl -sf "$BASE/api/palette" 2>/dev/null || echo "")
check "Returns valid JSON" bash -c "echo '$PALETTE' | python3 -c \"import json,sys; json.load(sys.stdin)\""
check "Has palette_id field" bash -c "echo '$PALETTE' | python3 -c \"import json,sys; assert 'palette_id' in json.load(sys.stdin)\""
check "Has entries array" bash -c "echo '$PALETTE' | python3 -c \"import json,sys; assert 'entries' in json.load(sys.stdin)\""
check "Has entry_count field" bash -c "echo '$PALETTE' | python3 -c \"import json,sys; assert 'entry_count' in json.load(sys.stdin)\""

# ── 7. MCP Endpoint Still Works ──
echo ""
echo "=== 7. MCP Endpoint ==="
check "MCP endpoint registered (from /)" bash -c "echo '$ROOT' | python3 -c \"import json,sys; assert json.load(sys.stdin).get('mcp_endpoint')=='/mcp'\""

# ── Summary ──
echo ""
echo "=== Results ==="
echo "  Passed: $pass"
echo "  Failed: $fail"
echo ""
echo "Manual browser test:"
echo "  http://localhost:${PORT}/viewer"
echo ""
echo "To test with model data:"
echo "  1. Start server: dotnet run --project src/VoxelForge.Mcp"
echo "  2. Use an MCP client to populate the model (set_voxels, load_model, etc.)"
echo "  3. Open http://localhost:5201/viewer in a browser"
echo "  4. Observe model rendered via Three.js WebGL"
echo "  5. Status overlay shows voxel count, mesh stats, and revision"
echo ""

if [ "$fail" -gt 0 ]; then
  echo "Some checks failed."
  exit 1
fi
echo "All checks passed."
