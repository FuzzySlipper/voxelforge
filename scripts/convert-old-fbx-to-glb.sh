#!/usr/bin/env bash
# convert-old-fbx-to-glb.sh — Convert old/legacy FBX (e.g. FBX 6100) to GLB for VoxelForge MCP reference workflow.
#
# Usage:
#   ./scripts/convert-old-fbx-to-glb.sh /path/to/model.fbx
#   ./scripts/convert-old-fbx-to-glb.sh /path/to/model.fbx --output content/mcp/imports/model.glb
#   ./scripts/convert-old-fbx-to-glb.sh /path/to/model.fbx --name my-model --format glb
#
# Environment:
#   FBX2GLTF_BIN       — Path to FBX2glTF executable (optional).
#   VOXELFORGE_IMPORT_DIR — Output base directory (default: content/mcp/imports).
#   VOXELFORGE_CACHE_DIR  — Bootstrap cache directory (default: /tmp/voxelforge/fbx-convert-cache).
#   VOXELFORGE_MCP_URL    — MCP endpoint for load hint (default: http://127.0.0.1:5201/mcp).

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# Defaults
OUTPUT=""
NAME=""
FORMAT="glb"
NO_BOOTSTRAP=false
CACHE_DIR="${VOXELFORGE_CACHE_DIR:-/tmp/voxelforge/fbx-convert-cache}"
IMPORT_DIR="${VOXELFORGE_IMPORT_DIR:-$REPO_ROOT/content/mcp/imports}"
MCP_URL="${VOXELFORGE_MCP_URL:-http://127.0.0.1:5201/mcp}"
FBX2GLTF_BIN="${FBX2GLTF_BIN:-}"

NPM_TARBALL_URL="https://registry.npmjs.org/fbx2gltf/-/fbx2gltf-0.9.7-p1.tgz"

help() {
  cat <<'EOF'
Usage: convert-old-fbx-to-glb.sh <source.fbx> [options]

Options:
  -o, --output PATH    Output file path (overrides --name and IMPORT_DIR).
  -n, --name NAME      Output base name without extension (default: source stem).
  -f, --format FORMAT  Output format: glb or gltf (default: glb).
      --no-bootstrap   Do not auto-download FBX2glTF from npm; fail if missing.
      --cache-dir DIR  Cache directory for downloaded converter (default: /tmp/voxelforge/fbx-convert-cache).
  -h, --help           Show this help.

Environment:
  FBX2GLTF_BIN              Path to FBX2glTF executable.
  VOXELFORGE_IMPORT_DIR     Default output directory.
  VOXELFORGE_CACHE_DIR      Bootstrap cache directory.
  VOXELFORGE_MCP_URL        MCP endpoint printed in follow-up hint.
EOF
}

die() { printf '%s\n' "$1" >&2; exit 1; }

# Parse arguments
SOURCE=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    -o|--output) OUTPUT="$2"; shift 2 ;;
    -n|--name)   NAME="$2";   shift 2 ;;
    -f|--format) FORMAT="$2"; shift 2 ;;
    --no-bootstrap) NO_BOOTSTRAP=true; shift ;;
    --cache-dir) CACHE_DIR="$2"; shift 2 ;;
    -h|--help) help; exit 0 ;;
    -*) die "Unknown option: $1" ;;
    *)
      if [[ -z "$SOURCE" ]]; then
        SOURCE="$1"
      else
        die "Unexpected extra argument: $1"
      fi
      shift
      ;;
  esac
done

[[ -n "$SOURCE" ]] || { help >&2; die "Error: source FBX path required"; }
[[ -f "$SOURCE" ]] || die "Error: source file not found: $SOURCE"

# Validate format
[[ "$FORMAT" == "glb" || "$FORMAT" == "gltf" ]] || die "Error: format must be glb or gltf"

# Derive output path
if [[ -n "$OUTPUT" ]]; then
  OUT_PATH="$OUTPUT"
else
  STEM="${NAME:-$(basename "$SOURCE")}"
  STEM="${STEM%.fbx}"
  STEM="${STEM%.FBX}"
  mkdir -p "$IMPORT_DIR"
  OUT_PATH="$IMPORT_DIR/${STEM}.${FORMAT}"
fi

OUT_DIR="$(dirname "$OUT_PATH")"
mkdir -p "$OUT_DIR"

# ── Detect FBX version ─────────────────────────────────────────────────────
FBX_INFO="$(file "$SOURCE" 2>/dev/null || true)"
FBX_HEADER=""
if [[ -n "$FBX_INFO" ]]; then
  FBX_HEADER="$FBX_INFO"
else
  # Fallback: read first bytes directly
  FBX_HEADER="$(head -c 27 "$SOURCE" | cat -v)"
fi

printf '=== VoxelForge Old-FBX -> GLB converter ===\n'
printf 'Source:      %s\n' "$SOURCE"
printf 'Detected:    %s\n' "$FBX_HEADER"
printf 'Output:      %s\n' "$OUT_PATH"

# ── Locate or bootstrap FBX2glTF ───────────────────────────────────────────────
CONVERTER=""

find_converter() {
  if [[ -n "$FBX2GLTF_BIN" && -x "$FBX2GLTF_BIN" ]]; then
    printf '%s\n' "$FBX2GLTF_BIN"
    return 0
  fi
  if command -v FBX2glTF >/dev/null 2>&1; then
    printf '%s\n' "$(command -v FBX2glTF)"
    return 0
  fi
  local cached
  cached="$CACHE_DIR/fbx2gltf-pkg/bin/Linux/FBX2glTF"
  if [[ -x "$cached" ]]; then
    printf '%s\n' "$cached"
    return 0
  fi
  return 1
}

bootstrap_converter() {
  if [[ "$NO_BOOTSTRAP" == true ]]; then
    return 1
  fi
  printf 'FBX2glTF not found. Bootstrapping from npm registry...\n' >&2
  mkdir -p "$CACHE_DIR"
  local tarball="$CACHE_DIR/fbx2gltf-0.9.7-p1.tgz"
  if [[ ! -f "$tarball" ]]; then
    curl -fsSL "$NPM_TARBALL_URL" -o "$tarball" || die "Failed to download FBX2glTF tarball"
  fi
  tar -xzf "$tarball" -C "$CACHE_DIR" || die "Failed to extract FBX2glTF tarball"
  mv "$CACHE_DIR/package" "$CACHE_DIR/fbx2gltf-pkg" || true
  local cached="$CACHE_DIR/fbx2gltf-pkg/bin/Linux/FBX2glTF"
  if [[ ! -x "$cached" ]]; then
    die "Bootstrapped FBX2glTF binary not found at $cached"
  fi
  printf 'Bootstrapped: %s\n' "$cached" >&2
  printf '%s\n' "$cached"
}

CONVERTER="$(find_converter)" || CONVERTER="$(bootstrap_converter)" || true

if [[ -z "$CONVERTER" || ! -x "$CONVERTER" ]]; then
  die "Error: FBX2glTF executable not found.

Setup options:
  1) Install FBX2glTF globally and ensure it is in PATH:
       npm install -g fbx2gltf
  2) Download the Linux binary directly and set FBX2GLTF_BIN:
       export FBX2GLTF_BIN=/path/to/FBX2glTF
  3) Let this script auto-bootstrap by removing --no-bootstrap and ensuring
     curl and tar are available.
"
fi

printf 'Converter:   %s\n' "$CONVERTER"

# ── Run conversion ───────────────────────────────────────────────────────────
OUT_STEM="${OUT_PATH%.*}"
FBX_ARGS=("--input" "$SOURCE" "--output" "$OUT_STEM")
if [[ "$FORMAT" == "glb" ]]; then
  FBX_ARGS+=("--binary")
fi

set +e
"$CONVERTER" "${FBX_ARGS[@]}"
CONV_EXIT=$?
set -e

if [[ $CONV_EXIT -ne 0 ]]; then
  die "Error: FBX2glTF conversion failed with exit code $CONV_EXIT"
fi

# FBX2glTF writes to OUT_STEM.glb or OUT_STEM.gltf depending on --binary
# Ensure the expected output path exists
if [[ "$FORMAT" == "glb" && ! -f "$OUT_PATH" && -f "${OUT_STEM}.glb" ]]; then
  mv "${OUT_STEM}.glb" "$OUT_PATH"
fi
if [[ "$FORMAT" == "gltf" && ! -f "$OUT_PATH" && -f "${OUT_STEM}.gltf" ]]; then
  mv "${OUT_STEM}.gltf" "$OUT_PATH"
fi

[[ -f "$OUT_PATH" ]] || die "Error: expected output file not found after conversion: $OUT_PATH"

OUT_SIZE="$(stat -c%s "$OUT_PATH" 2>/dev/null || stat -f%z "$OUT_PATH" 2>/dev/null || echo "?")"
printf 'Converted:   %s (%s bytes)\n' "$OUT_PATH" "$OUT_SIZE"

# ── Print follow-up MCP workflow ───────────────────────────────────────────
printf '\n=== Next steps ===\n'
printf 'Load the converted model into VoxelForge MCP:\n'
printf '  MCP tool: load_reference_model\n'
printf '  { "path": "%s" }\n' "$OUT_PATH"
printf '\nSuggested workflow:\n'
printf '  1) load_reference_model  -> { "path": "%s" }\n' "$OUT_PATH"
printf '  2) list_reference_models   -> {}\n'
printf '  3) transform_reference_model -> { "index": 0, "scale": 10 }  (tune scale for voxelization density)\n'
printf '  4) voxelize_reference_model -> { "index": 0, "resolution": 64, "mode": "solid" }\n'
printf '  5) publish_preview         -> { "name": "<preview-name>" }\n'
printf '\nScale note: old FBX assets are often authored in centimeters or arbitrary units.\n'
printf 'If voxelization produces too few voxels, increase scale (e.g. 5-20) before voxelizing.\n'
