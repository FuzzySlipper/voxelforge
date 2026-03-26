#!/usr/bin/env bash
set -euo pipefail

# Build FNA native libraries (FNA3D, FAudio) from source against system SDL3.
# Prerequisites: cmake, make, gcc, SDL3 dev headers (pacman -S sdl3)
#
# Usage:
#   ./build-native.sh          # Build and copy to output directory
#   ./build-native.sh clean    # Remove build directories

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
FNA_LIB="$SCRIPT_DIR/lib/FNA/lib"
OUTPUT_DIR="$SCRIPT_DIR/src/VoxelForge.Engine.MonoGame/bin/Debug/net10.0"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

log()   { echo -e "${GREEN}[build-native]${NC} $*"; }
warn()  { echo -e "${YELLOW}[build-native]${NC} $*"; }
error() { echo -e "${RED}[build-native]${NC} $*" >&2; }

check_prereqs() {
    local missing=()
    command -v cmake  >/dev/null 2>&1 || missing+=(cmake)
    command -v make   >/dev/null 2>&1 || missing+=(make)
    command -v gcc    >/dev/null 2>&1 || missing+=(gcc)
    pkg-config --exists sdl3 2>/dev/null || missing+=(sdl3)

    if [[ ${#missing[@]} -gt 0 ]]; then
        error "Missing prerequisites: ${missing[*]}"
        error "Install with: pacman -S ${missing[*]}"
        exit 1
    fi
}

check_submodules() {
    if [[ ! -f "$FNA_LIB/FNA3D/CMakeLists.txt" ]] || [[ ! -f "$FNA_LIB/FAudio/CMakeLists.txt" ]]; then
        error "FNA submodules not initialized. Run:"
        error "  git submodule update --init --recursive"
        exit 1
    fi
}

build_fna3d() {
    log "Building FNA3D..."
    local build_dir="$FNA_LIB/FNA3D/build"
    mkdir -p "$build_dir"
    cmake -S "$FNA_LIB/FNA3D" -B "$build_dir" \
        -DCMAKE_BUILD_TYPE=Release \
        -DSDL3_DIR=/usr \
        > /dev/null 2>&1
    make -C "$build_dir" -j"$(nproc)" > /dev/null 2>&1

    if [[ -f "$build_dir/libFNA3D.so.0" ]]; then
        log "FNA3D built successfully"
    else
        error "FNA3D build failed"
        exit 1
    fi
}

build_faudio() {
    log "Building FAudio..."
    local build_dir="$FNA_LIB/FAudio/build"
    mkdir -p "$build_dir"
    cmake -S "$FNA_LIB/FAudio" -B "$build_dir" \
        -DCMAKE_BUILD_TYPE=Release \
        > /dev/null 2>&1
    make -C "$build_dir" -j"$(nproc)" > /dev/null 2>&1

    if [[ -f "$build_dir/libFAudio.so.0" ]]; then
        log "FAudio built successfully"
    else
        error "FAudio build failed"
        exit 1
    fi
}

copy_to_output() {
    mkdir -p "$OUTPUT_DIR"
    cp "$FNA_LIB/FNA3D/build/libFNA3D.so.0" "$OUTPUT_DIR/"
    cp "$FNA_LIB/FAudio/build/libFAudio.so.0" "$OUTPUT_DIR/"
    log "Native libs copied to $OUTPUT_DIR"
}

clean() {
    log "Cleaning native build directories..."
    rm -rf "$FNA_LIB/FNA3D/build"
    rm -rf "$FNA_LIB/FAudio/build"
    log "Clean complete"
}

if [[ "${1:-}" == "clean" ]]; then
    clean
    exit 0
fi

check_prereqs
check_submodules
build_fna3d
build_faudio
copy_to_output

log "Done. Run the app with:"
log "  dotnet run --project src/VoxelForge.Engine.MonoGame"
