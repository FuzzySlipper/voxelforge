#!/usr/bin/env bash
set -euo pipefail

# Runner/debugging wrapper for the live Plasma/KWin Electron menu smoke.
# uvx supplies kwin-mcp in an isolated Python environment, while the script
# itself records screenshots/logs under artifacts/live-kwin-menu-smoke/.

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
ELECTRON_DIR="$(cd -- "${SCRIPT_DIR}/.." && pwd)"

cd "${ELECTRON_DIR}"
exec uvx --from kwin-mcp python "${SCRIPT_DIR}/live-kwin-menu-smoke.py" --electron-dir "${ELECTRON_DIR}" "$@"
