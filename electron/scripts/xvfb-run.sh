#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# xvfb-run.sh — Xvfb wrapper for headless VoxelForge Electron testing.
#
# Checks for an active DISPLAY; if none found, starts Xvfb on display :99,
# runs the given command, then cleans up.
#
# Usage: ./scripts/xvfb-run.sh <command...>
#
# Example: ./scripts/xvfb-run.sh npx vitest run tests/menu-gui-smoke.test.ts
#          ./scripts/xvfb-run.sh npm run smoke-test
#
# On CI or headless hosts without Xvfb installed, this will fail with a
# clear message rather than silently producing a no-op result.
# ---------------------------------------------------------------------------
set -euo pipefail

if [ -n "${DISPLAY:-}" ]; then
  # A display is already available (e.g. desktop session, CI Xvfb).
  exec "$@"
fi

if ! command -v Xvfb &>/dev/null; then
  cat >&2 <<'ERR'

  ERROR: Headless display required but not available.

  This test requires a display server (Xvfb or real X display) to launch
  Electron with a BrowserWindow. On headless/dev machines, install Xvfb:

    sudo apt install xvfb          # Debian/Ubuntu
    sudo pacman -S xorg-server-xvfb # Arch
    sudo dnf install xorg-x11-server-Xvfb # Fedora

  Then run via:
    ./scripts/xvfb-run.sh <command>

  Or set DISPLAY=:99 and start Xvfb manually:
    Xvfb :99 -screen 0 1920x1080x24 &
    export DISPLAY=:99
    <command>
ERR
  exit 1
fi

XVFB_DISPLAY="${XVFB_DISPLAY:-:99}"
XVFB_SCREEN="${XVFB_SCREEN:-0 1920x1080x24}"

echo "[xvfb-run] Starting Xvfb on display ${XVFB_DISPLAY}..."
Xvfb "${XVFB_DISPLAY}" -screen "${XVFB_SCREEN}" &
XVFB_PID=$!

# Wait for Xvfb to be ready
sleep 1

export DISPLAY="${XVFB_DISPLAY}"
echo "[xvfb-run] Display ready (PID ${XVFB_PID}). Running: $*"

set +e
"$@"
EXIT_CODE=$?
set -e

echo "[xvfb-run] Command exited with code ${EXIT_CODE}. Cleaning up Xvfb..."
kill "${XVFB_PID}" 2>/dev/null || true
# Give Xvfb time to release its lock
sleep 0.5

exit "${EXIT_CODE}"
