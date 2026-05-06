#!/usr/bin/env bash
# ── VoxelForge Renderer Performance Benchmark Runner ──
#
# Usage:
#   ./scripts/run-renderer-benchmark.sh [options]
#
# Options:
#   --extralarge    Include the 64^3 dense checkerboard scene (default: off)
#   --output DIR    Output directory (default: /tmp/voxelforge/renderer-benchmark-<timestamp>)
#   --warmup N      Warmup iterations before measurement (default: 2)
#   --trials N      Measurement trials per metric (default: 5)
#   --electron      Also run the Electron renderer smoke test (if display available)
#   --help          Show this help
#
# Output:
#   <output-dir>/csharp-benchmark.json      — C# mesh/snapshot/mutation metrics
#   <output-dir>/electron-smoke.json        — Electron scene construction metrics (if --electron)
#   <output-dir>/combined-summary.md        — Human-readable summary
#
# Prerequisites:
#   - dotnet SDK
#   - Built solution: dotnet build voxelforge.slnx
#   - For --electron: Node.js, npm, electron, display server (or Xvfb)
#
# This script does NOT require GPU. The C# benchmark is fully headless.
# The Electron smoke test requires a display server but uses
# --disable-gpu and falls back gracefully in headless environments.

set -euo pipefail

cd "$(dirname "$0")/.."

# ── Parse options ──
EXTRALARGE=""
WARMUP=2
TRIALS=5
RUN_ELECTRON=false
OUTPUT_DIR=""

while [ $# -gt 0 ]; do
  case "$1" in
    --extralarge) EXTRALARGE="--extralarge"; shift ;;
    --output) OUTPUT_DIR="$2"; shift 2 ;;
    --warmup) WARMUP="$2"; shift 2 ;;
    --trials) TRIALS="$2"; shift 2 ;;
    --electron) RUN_ELECTRON=true; shift ;;
    --help|-h)
      sed -n '2,30p' "$0"
      exit 0
      ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

if [ -z "$OUTPUT_DIR" ]; then
  TIMESTAMP=$(date -u +%Y%m%dT%H%M%SZ)
  OUTPUT_DIR="/tmp/voxelforge/renderer-benchmark-${TIMESTAMP}"
fi
mkdir -p "$OUTPUT_DIR"

echo "=== VoxelForge Renderer Benchmark ==="
echo "Output:  $OUTPUT_DIR"
echo "Warmup:  $WARMUP"
echo "Trials:  $TRIALS"
echo "ExtraXL: ${EXTRALARGE:-no}"
echo ""

# ── 1. C# benchmark ──
echo "--- Running C# renderer benchmark ---"
CSV_JSON="${OUTPUT_DIR}/csharp-benchmark.json"
dotnet run --project src/VoxelForge.Evaluation -- renderer-benchmark \
  --warmup "$WARMUP" --trials "$TRIALS" $EXTRALARGE \
  > "$CSV_JSON" 2> >(tee "${OUTPUT_DIR}/csharp-benchmark-stderr.log" >&2)

if [ $? -ne 0 ]; then
  echo "C# benchmark failed." >&2
  exit 1
fi

# Print a quick summary line per scene
python3 << EOF
import json
data = json.load(open("${CSV_JSON}"))
for s in data["scenes"]:
    g = s["greedy_mesher"]
    snap = s["mesh_snapshot"]
    print(f"  {s['scene_id']}: {s['actual_voxel_count']} voxels, {g['triangle_count']} tri (greedy), {g['median_ms']}ms mesh, {snap['estimated_json_bytes']} bytes snapshot")
EOF

echo "Wrote C# benchmark: $CSV_JSON"
echo ""

# ── 2. Electron renderer smoke test (optional) ──
if [ "$RUN_ELECTRON" = true ]; then
  echo "--- Running Electron renderer smoke test ---"
  ELEC_JSON="${OUTPUT_DIR}/electron-smoke.json"
  echo "{}" > "$ELEC_JSON"

  # Build the C# sidecar first
  echo "Building sidecar..."
  dotnet build src/VoxelForge.Bridge/VoxelForge.Bridge.csproj -q

  # Install electron dependencies if needed
  if [ ! -d "electron/node_modules" ]; then
    echo "Installing Electron dependencies..."
    (cd electron && npm install)
  fi

  # Try to run with xvfb-run if available (headless CI), otherwise native
  XVFB=""
  if command -v xvfb-run &>/dev/null; then
    XVFB="xvfb-run --auto-servernum"
    echo "Using xvfb-run for headless display."
  else
    echo "xvfb-run not found; running Electron with --disable-gpu directly."
    echo "(May fail if no display server is available.)"
  fi

  # Run the renderer smoke test, capture stdout
  set +e
  $XVFB electron/node_modules/.bin/electron electron/dist/main/index.js \
    --renderer-smoke-test 2>&1 | tee "${OUTPUT_DIR}/electron-smoke-raw.log"
  ELEC_EXIT=$?
  set -e

  if [ $ELEC_EXIT -eq 0 ]; then
    python3 << EOF
import json, re
log = open("${OUTPUT_DIR}/electron-smoke-raw.log").read()
m = re.search(r'Renderer metrics:\s*(\{.+?\})', log)
if m:
    metrics = json.loads(m.group(1))
    json.dump(metrics, open("${ELEC_JSON}", "w"), indent=2)
    print(f"Electron metrics: {json.dumps(metrics, indent=2)}")
else:
    print("Renderer metrics not found in log output")
    json.dump({"status": "no_metrics_found"}, open("${ELEC_JSON}", "w"))
EOF
    echo "Wrote Electron smoke metrics: $ELEC_JSON"
  else
    echo "Electron smoke test exited with code $ELEC_EXIT (expected without display)."
    python3 -c "import json; json.dump({'status':'skipped','exit_code':${ELEC_EXIT},'reason':'no_display_or_GPU'}, open('${ELEC_JSON}','w'))"
  fi
  echo ""
fi

# ── 3. Generate summary ──
SUMMARY="${OUTPUT_DIR}/combined-summary.md"
python3 scripts/renderer-benchmark-summary.py \
  "$CSV_JSON" \
  "${OUTPUT_DIR}/electron-smoke.json" \
  "$SUMMARY"

echo ""
echo "=== Renderer benchmark complete ==="
echo "Results in: $OUTPUT_DIR"
echo "Summary:    $SUMMARY"
