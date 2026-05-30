# VoxelForge Electron — Menu GUI Smoke Test

This directory implements a **real-Electron integration test** for native menu
commands, going beyond the static parity checks in `tests/menu-bridge-parity.test.ts`.

## Why

The existing parity tests verify that menu channels, preload channels, and IPC
handlers are aligned statically. They do **not** launch a real `BrowserWindow`,
click native menu items, or verify that IPC events actually flow from:

    native menu click → main process webContents.send()
                      → preload ipcRenderer.on()
                      → renderer handler
                      → prompt/dialog
                      → bridge request

Task #1720 reports that most native menu commands appear inert in the live app
(no error, no dialog, no console output). This smoke test reproduces that
chain programmatically and validates it.

## Running

```bash
# Prerequisite: display server (Xvfb on headless)
cd electron

# Option 1: auto-detect display/Xvfb (recommended)
node scripts/run-menu-gui-smoke.mjs

# Option 2: via xvfb-run.sh wrapper
./scripts/xvfb-run.sh node scripts/run-menu-gui-smoke.mjs

# Option 3: via npm (also runs build first)
npm run smoke-test:menu

# Option 4: direct Electron launch (requires DISPLAY)
npx electron scripts/menu-gui-smoke.mjs
```

## Runner live KWin smoke loop

Task #1732 adds a separate **live visual** workflow for Runner debugging on the
`agent` Plasma/KWin session. This is not a CI replacement for the deterministic
smoke above; it is a screenshot/log feedback loop for the failure mode where a
native menu item is visible and clickable but appears to do nothing.

```bash
cd electron

# Recommended Runner command. Builds first, launches Electron in the live
# KWin/Wayland session, drives Reference > Load Reference Model..., and writes
# screenshots + logs under ../artifacts/live-kwin-menu-smoke/<timestamp>/.
npm run smoke-test:live-kwin

# Faster iteration after a recent build:
scripts/run-live-kwin-menu-smoke.sh --no-build

# Coordinate fallback if Alt+R menu mnemonics stop working in the live desktop:
scripts/run-live-kwin-menu-smoke.sh --menu-mode coordinate \
  --reference-menu-x 134 --reference-menu-y 34 \
  --load-item-x 172 --load-item-y 64
```

The live workflow expects the Runner host's real desktop session:

- `DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/1001/bus`
- `WAYLAND_DISPLAY=wayland-0`
- `DISPLAY=:0`
- `XDG_RUNTIME_DIR=/run/user/1001`

It applies a local Python compatibility shim before creating the KWin EIS input
backend because the current `kwin-mcp`/`libei` combination on this host can crash
when binding variadic seat capabilities without explicit ctypes values. The shim
uses `ctypes.c_int(...)` capability args and a `ctypes.c_void_p(None)` sentinel.

Evidence produced on every run:

- `01-baseline.png` — live Electron window before menu driving.
- `02-prompt-visible.png` — renderer-owned Reference Model path prompt after the native menu action.
- `03-after-reference-load-submit.png` — after submitting the reference-model path prompt.
- `04-final.png` or `failure.png` — final/failure-visible desktop state.
- `electron-live-kwin.log` — Electron stdout/stderr plus forwarded renderer console logs.
- `summary.json` — menu path, coordinates/keys, screenshot paths, KWin connection info,
  and booleans for the expected signals.

The PASS condition is intentionally narrow: the log must show the renderer
received `menu:reference-model-load`, accepted the typed path, and attempted the
`bridge:myra-command-execute` `refload` request. If any signal is missing, the
script exits nonzero but still writes before/after screenshots, clicked/keyed
path details, app log, and summary JSON for debugging.

V1 caveat: AT-SPI/window enumeration in this live session currently only sees
`xdg-desktop-portal-gtk`, so the workflow uses screenshots plus keyboard/menu
coordinates rather than semantic UI selectors. Treat coordinate mode as brittle
and adjust the `--reference-menu-*` / `--load-item-*` values from the baseline
screenshot when the desktop theme, scale, or window placement changes.

## What it tests

1. **Menu structure**: 6 top-level menus exist (File, Edit, Reference, View,
   Tools, Help)

2. **Menu click→IPC dispatch**: programmatically clicks representative items
   from each menu and verifies `webContents.send()` was called with the correct
   channel:
   - Reference > Load Reference Model... → `menu:reference-model-load`
   - Reference > List Reference Models → `menu:reference-model-list`
   - File > New → `menu:file-new`
   - File > Open... → `menu:file-open`
   - File > Save → `menu:file-save`
   - Edit > Undo → `menu:edit-undo`
   - Edit > Redo → `menu:edit-redo`
   - View > Front → `menu:view-front`
   - View > Wireframe Toggle → `menu:view-wireframe`
   - Tools > Voxelize... → `menu:voxelize-execute`
   - Help > About VoxelForge → `menu:help-about`

3. **No-click-throws**: every clickable menu item is exercised to verify no
   exception is thrown during dispatch.

4. **Channel validity**: all sent channels are checked against the
   `MenuChannels` constants.

## Renderer diagnostics added

Every renderer menu event handler in `src/renderer/index.ts` now logs:

- `[renderer] Menu event received: <channel>` — confirms IPC event arrived
- `[renderer] menu:<channel> cancelled by user` — when prompt/dialog is dismissed
- `[renderer] bridge:myra-command-execute request/response` — confirms menu-driven
  Myra commands reached the bridge path and returned a result or error

The live renderer also uses a DOM-owned path prompt for `Reference > Load
Reference Model...` because Electron's built-in `window.prompt()` is not
available in the current live app runtime. All other interactive menu commands
(File > Open, Edit > Fill Region, View > Background Color, etc.) use
`window.prompt` synchronously through the shared `menuCommandHandlers`
dispatch table used by both native IPC and the accessible menu surface.
This makes the prompt visible in KWin
screenshots and keeps the handler testable through dependency injection.

This makes it possible to see in Electron dev tools console exactly where the
chain breaks: was the event received but cancelled? Was the `bridge:myra-command-execute`
sent but never answered?

## Headless requirement

On Linux without a display server, Electron cannot create `BrowserWindow`
instances. Use the `xvfb-run.sh` wrapper or the launcher script to
auto-detect and start Xvfb.

**If Xvfb is not installed**, the launcher will fail with a clear message:

```
ERROR: No display server available and Xvfb is not installed.
```

Install Xvfb:

```bash
sudo apt install xvfb            # Debian/Ubuntu
sudo pacman -S xorg-server-xvfb  # Arch
sudo dnf install xorg-x11-server-Xvfb  # Fedora
```
