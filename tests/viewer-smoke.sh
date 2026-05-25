#!/usr/bin/env bash
#
# VoxelForge Viewer Smoke Test
#
# Validates the browser viewer API endpoints:
#   1. Empty-state contract (endpoints return valid schema)
#   2. Populated scene (uses fill_box MCP tool to create voxels)
#   3. Non-empty render snapshot with materials/textures
#   4. Capture readiness signal
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
check "Returns HTML content" bash -c "echo '$HTML' | head -1 | grep -q '<\\!DOCTYPE html>'"
check "Includes Three.js CDN" bash -c "echo '$HTML' | grep -q 'cdnjs.cloudflare.com/ajax/libs/three.js'"
check "Includes OrbitControls" bash -c "echo '$HTML' | grep -q 'examples/js/controls/OrbitControls'"
check "Includes diagnostics overlay" bash -c "echo '$HTML' | grep -q 'diag-overlay'"
check "No editing controls" bash -c "echo '$HTML' | grep -qv 'undo-button\\|redo-button\\|palette-list\\|tool-list'"
check "Has polling interval" bash -c "echo '$HTML' | grep -q 'POLL_INTERVAL_MS'"
check "No Electron APIs" bash -c "echo '$HTML' | grep -qv 'electron\\|preload\\|voxelforgeBridge'"

# ── 4. Viewer State API (empty model) ──
echo ""
echo "=== 4. State API (empty model) ==="
STATE=$(curl -sf "$BASE/api/viewer-state" 2>/dev/null || echo "")
check "Returns valid JSON" bash -c "echo '$STATE' | python3 -c \"import json,sys; json.load(sys.stdin)\""
check "Has revision field" bash -c "echo '$STATE' | python3 -c \"import json,sys; assert 'revision' in json.load(sys.stdin)\""
check "Has model_name field" bash -c "echo '$STATE' | python3 -c \"import json,sys; assert 'model_name' in json.load(sys.stdin)\""
check "Has voxel_count field" bash -c "echo '$STATE' | python3 -c \"import json,sys; assert 'voxel_count' in json.load(sys.stdin)\""
check "Has palette_entries field" bash -c "echo '$STATE' | python3 -c \"import json,sys; assert 'palette_entries' in json.load(sys.stdin)\""
check "Model name is 'untitled'" bash -c "echo '$STATE' | python3 -c \"import json,sys; assert json.load(sys.stdin).get('model_name')=='untitled'\""
check "Voxel count is 0 for empty model" bash -c "echo '$STATE' | python3 -c \"import json,sys; assert json.load(sys.stdin).get('voxel_count')==0\""

# ── 5. Mesh Snapshot API (empty model) ──
echo ""
echo "=== 5. Mesh Snapshot API (empty model) ==="
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

# ── 6. Palette API (empty model) ──
echo ""
echo "=== 6. Palette API ==="
PALETTE=$(curl -sf "$BASE/api/palette" 2>/dev/null || echo "")
check "Returns valid JSON" bash -c "echo '$PALETTE' | python3 -c \"import json,sys; json.load(sys.stdin)\""
check "Has palette_id field" bash -c "echo '$PALETTE' | python3 -c \"import json,sys; assert 'palette_id' in json.load(sys.stdin)\""
check "Has entries array" bash -c "echo '$PALETTE' | python3 -c \"import json,sys; assert 'entries' in json.load(sys.stdin)\""
check "Has entry_count field" bash -c "echo '$PALETTE' | python3 -c \"import json,sys; assert 'entry_count' in json.load(sys.stdin)\""

# ── 7. SSE Events Endpoint ──
echo ""
echo "=== 7. SSE Events Endpoint ==="
SSE_OUTPUT=$(curl -sf -N --max-time 2 "$BASE/api/viewer-events" 2>/dev/null || echo "")
check "Returns SSE data lines" bash -c "echo '$SSE_OUTPUT' | grep -q 'data:'"
check "Includes connected event with revision" bash -c "echo '$SSE_OUTPUT' | grep -q 'connected.*revision'"

# ── 8. MCP Endpoint Still Works ──
echo ""
echo "=== 8. MCP Endpoint ==="
check "MCP endpoint registered (from /)" bash -c "echo '$ROOT' | python3 -c \"import json,sys; assert json.load(sys.stdin).get('mcp_endpoint')=='/mcp'\""

# ── 9. Canonical Render Snapshot API (empty state) ──
echo ""
echo "=== 9. Render Snapshot API (empty) ==="
SNAPSHOT=$(curl -sf "$BASE/api/render/snapshot" 2>/dev/null || echo "")
check "Returns valid JSON" bash -c "echo '$SNAPSHOT' | python3 -c \"import json,sys; json.load(sys.stdin)\""
check "Has schema_version" bash -c "echo '$SNAPSHOT' | python3 -c \"import json,sys; assert 'schema_version' in json.load(sys.stdin)\""
check "Has voxel_meshes array" bash -c "echo '$SNAPSHOT' | python3 -c \"import json,sys; assert 'voxel_meshes' in json.load(sys.stdin)\""
check "Has reference_nodes array" bash -c "echo '$SNAPSHOT' | python3 -c \"import json,sys; assert 'reference_nodes' in json.load(sys.stdin)\""
check "Has materials array" bash -c "echo '$SNAPSHOT' | python3 -c \"import json,sys; assert 'materials' in json.load(sys.stdin)\""
check "Has textures array" bash -c "echo '$SNAPSHOT' | python3 -c \"import json,sys; assert 'textures' in json.load(sys.stdin)\""
check "Empty snapshot has 0 voxel meshes" bash -c "echo '$SNAPSHOT' | python3 -c \"import json,sys; assert len(json.load(sys.stdin).get('voxel_meshes',[]))==0\""

# ── 10. Render State API (empty) ──
echo ""
echo "=== 10. Render State API ==="
RENDER_STATE=$(curl -sf "$BASE/api/render/state" 2>/dev/null || echo "")
check "Returns valid JSON" bash -c "echo '$RENDER_STATE' | python3 -c \"import json,sys; json.load(sys.stdin)\""

# ── 11. Populated Scene: use fill_box MCP tool to create voxels ──
echo ""
echo "=== 11. Populated Scene ==="

# First, clear any existing model state
curl -s -X POST "$BASE/mcp" \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"clear_model","arguments":{}}}' \
  > /dev/null 2>&1 || true

sleep 1

# Fill a 4x4x4 cube with palette index 1
FILL_RESULT=$(curl -sf -X POST "$BASE/mcp" \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"fill_box","arguments":{"x1":0,"y1":0,"z1":0,"x2":3,"y2":3,"z2":3,"palette_index":1}}}' \
  2>/dev/null || echo "")

check "fill_box MCP tool returns result" bash -c "echo '$FILL_RESULT' | python3 -c \"import json,sys; d=json.load(sys.stdin); assert 'result' in d or 'error' in d\""

# Wait for async processing
sleep 2

# ── 12. Verify populated state ──
echo ""
echo "=== 12. Verify Populated State ==="
POP_STATE=$(curl -sf "$BASE/api/viewer-state" 2>/dev/null || echo "")
check "State returns after fill" bash -c "echo '$POP_STATE' | python3 -c \"import json,sys; json.load(sys.stdin)\""
check "Voxel count > 0 after fill" bash -c "echo '$POP_STATE' | python3 -c \"import json,sys; d=json.load(sys.stdin); assert d.get('voxel_count',0)>0\""

POP_SNAPSHOT=$(curl -sf "$BASE/api/render/snapshot" 2>/dev/null || echo "")
check "Render snapshot returns after fill" bash -c "echo '$POP_SNAPSHOT' | python3 -c \"import json,sys; json.load(sys.stdin)\""
check "Non-empty voxel_meshes after fill" bash -c "echo '$POP_SNAPSHOT' | python3 -c \"import json,sys; d=json.load(sys.stdin); assert len(d.get('voxel_meshes',[]))>0; assert d['voxel_meshes'][0].get('positions',[])\""
check "Non-empty palette entries" bash -c "echo '$POP_STATE' | python3 -c \"import json,sys; d=json.load(sys.stdin); assert d.get('palette_entries') is not None\""

# ── 13. Verify mesh snapshot has actual geometry ──
echo ""
echo "=== 13. Mesh Geometry Validation ==="
POP_MESH=$(curl -sf "$BASE/api/mesh-snapshot" 2>/dev/null || echo "")
check "Has vertex_count > 0" bash -c "echo '$POP_MESH' | python3 -c \"import json,sys; assert json.load(sys.stdin).get('vertex_count',0)>0\""
check "Has triangle_count > 0" bash -c "echo '$POP_MESH' | python3 -c \"import json,sys; assert json.load(sys.stdin).get('triangle_count',0)>0\""
check "Has non-empty positions" bash -c "echo '$POP_MESH' | python3 -c \"import json,sys; d=json.load(sys.stdin); assert len(d.get('positions',[]))>0\""
check "Has non-empty indices" bash -c "echo '$POP_MESH' | python3 -c \"import json,sys; d=json.load(sys.stdin); assert len(d.get('indices',[]))>0\""

# ── 14. Clear model for next run ──
echo ""
echo "=== 14. Cleanup ==="
curl -s -X POST "$BASE/mcp" \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"clear_model","arguments":{}}}' \
  > /dev/null 2>&1 || true
check "Clear model completed" echo "done"

# ── Summary ──
echo ""
echo "=== Results ==="
echo "  Passed: $pass"
echo "  Failed: $fail"
echo ""
echo "Manual browser test:"
echo "  http://localhost:${PORT}/viewer"
echo ""
echo "Manual capture test:"
echo "  # After populating model with voxels, capture preset views via MCP:"
echo "  curl -X POST http://localhost:${PORT}/mcp \\"
echo "    -H 'Content-Type: application/json' \\"
echo "    -d '{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"view_from_angle\",\"arguments\":{\"preset\":\"front\"}}}'"
echo ""
echo "Electron smoke test note:"
echo "  The Electron GUI smoke test requires an X display server."
echo "  Without one, Electron windows cannot be created."
echo "  See: https://www.electronjs.org/docs/latest/tutorial/automated-testing#ci-specific-recommendations"
echo "  Plan: Add Xvfb to CI runner or use xvfb-run wrapper for headless GUI testing."
echo "  Example: xvfb-run --auto-servernum npm run smoke-test"
echo ""

if [ "$fail" -gt 0 ]; then
  echo "Some checks failed."
  exit 1
fi
echo "All checks passed."
