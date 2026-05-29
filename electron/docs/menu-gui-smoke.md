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
