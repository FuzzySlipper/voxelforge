#!/usr/bin/env bash
#
# run-mcp-traffic-benchmark.sh - Wrapper for MCP traffic/token ranging.
#
# Ensures the MCP web server is running, then runs the benchmark script.
#
# Usage:
#   ./scripts/run-mcp-traffic-benchmark.sh [benchmark args...]
#
# Without arguments, runs the default "all" suite (small + medium + large smoke).
# Pass "--" plus benchmark args to override:
#   ./scripts/run-mcp-traffic-benchmark.sh -- --mode smoke --size small
#
# Output goes to /tmp/voxelforge/ by default.
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

OUTPUT_DIR="${VOXELFORGE_TMP_DIR:-/tmp/voxelforge}"
MCP_URL="${VOXELFORGE_MCP_URL:-http://localhost:5201/mcp}"

# Ensure MCP server is running
echo "=== Starting MCP server if not already running ==="
"$SCRIPT_DIR/ensure-mcp-web.sh"

# Check server health
echo "=== Checking server health ==="
if ! curl -sf http://localhost:5201/health > /dev/null 2>&1; then
    echo "ERROR: MCP server is not healthy after ensure-mcp-web.sh" >&2
    echo "Check logs: /tmp/voxelforge/mcp-web.log" >&2
    exit 1
fi

echo "=== MCP server is healthy ==="
echo ""

# Default args
if [ $# -eq 0 ]; then
    set -- all
fi

# Run benchmark
echo "=== Running MCP traffic benchmark ==="
echo "  MCP URL:    $MCP_URL"
echo "  Output dir: $OUTPUT_DIR"
echo "  Args:       $*"
echo ""

python3 "$SCRIPT_DIR/mcp-traffic-benchmark.py" \
    --mcp-url "$MCP_URL" \
    --output-dir "$OUTPUT_DIR" \
    "$@"

echo ""
echo "=== Done ==="
echo "Output artifacts in: $OUTPUT_DIR"
