# VoxelForge Electron — Accessibility & GUI Test Harness

## Overview

Task #1740 introduces first-class accessibility to the Electron UI and a
Playwright-based LLM/GUI harness. Task #1743 replaces native workflow menus with
a renderer-owned accessible menu surface.

## Test Layers (recommended)

### 1. Deterministic Unit/Bridge Tests (`tests/`)

**What:** Vitest tests run without Electron (`vitest run`). Fast (~400ms for
260 tests), reliable, CI-gate-keeping.

**Coverage:**
- `tests/menu-handlers.test.ts` — handler logic with mocked deps
- `tests/menu-bridge-parity.test.ts` — IPC channel alignment, C# command parity
- `tests/accessibility-contract.test.ts` — #1740: ARIA roles, labels, menu
  model structure in source files

**How to run:**
```bash
cd electron
npm test               # vitest run
npm run test:watch     # vitest in watch mode
```

### 2. Playwright Electron Accessibility Harness (`scripts/run-accessible-gui-smoke.mjs`)

**What:** Launches the actual Electron app via Playwright, gets the
BrowserWindow/page, captures screenshots, accessibility snapshots, DOM locator
inventory, and console logs. Writes a compact JSON report under
`../artifacts/accessible-gui-smoke/<timestamp>/`.

**Coverage:**
- App launches to main UI with accessible menubar
- All 6 expected menus (File, Edit, Reference, View, Tools, Help) present
- Reference > Load Reference Model... prompt accessible through role locators
- Screenshots captured at each workflow step
- Full accessibility tree snapshot via Playwright CDP

**How to run:**
```bash
cd electron
npm run gui:llm-smoke              # with X display
ARTIFACT_DIR=/tmp/my-artifacts node scripts/run-accessible-gui-smoke.mjs
```

**Prerequisites:**
- Display server (Xvfb on headless: `apt install xvfb`)
- Build first: `npm run build`
- Playwright Chromium: `npx playwright install chromium`

**Output:**
```
artifacts/accessible-gui-smoke/<timestamp>/
├── report.json       # Full structured report
├── report.md         # Compact Markdown summary
├── 01-baseline.png
├── 02-reference-menu-open.png
├── 03-reference-prompt-visible.png
├── 04-path-filled.png
├── 05-after-submit.png
└── 06-final.png
```

### 3. KWin/FlaUI OS-Level Coverage (`scripts/run-live-kwin-menu-smoke.sh`, etc.)

**What:** Drives the OS-level native Electron menu via KWin/EIS input simulation
or FlaUI (Windows). Covers native menu chrome and window management that the
in-process harness cannot reach.

**How to run:**
```bash
cd electron
npm run smoke-test:live-kwin        # KWin Wayland session
```

**Reference:** `electron/docs/menu-gui-smoke.md`

## Accessible Menu Surface

### ARIA Roles & Keyboard Navigation

The renderer-owned menu surface (`AccessibleMenuSurface`) uses:

| Element         | Role / Attribute                | Keyboard                              |
|-----------------|---------------------------------|---------------------------------------|
| Menu bar        | `role="menubar"`                | Tab to enter, Left/Right navigate     |
| Top-level item  | `role="menuitem"`               | Enter/Space/ArrowDown opens submenu   |
| Submenu         | `role="menu"`                   | Up/Down navigate, Escape closes       |
| Submenu item    | `role="menuitem"`               | Enter/Space activates                 |
| Separator       | `role="separator"`              | —                                     |
| Toggle buttons  | `aria-pressed`                  | —                                     |
| Status/errors   | `role="status"` + `aria-live`   | —                                     |

### Focus Management

- Roving tabindex: only one menubar item has `tabindex="0"` at a time
- `:focus-visible` outline on all interactive elements
- Skip link (`.skip-link`) at top of page to bypass menu bar
- Submenu close on focus-out with 100ms delay for click handlers

### Shared Menu Model

`src/shared/menu-command-model.ts` defines `APP_MENU_MODEL` — the single
authoritative menu structure. Both the renderer accessible surface and the
retained native menu consume this model, preventing divergent menu definitions.

To add a new menu item:
1. Add to `MenuChannels` in `src/shared/menu-channels.ts`
2. Add to `APP_MENU_MODEL` in `src/shared/menu-command-model.ts`
3. Add handler in `dispatchMenuCommand()` in `src/renderer/index.ts`
4. Add to `allowedEventChannels` in `src/preload/index.ts` if main→renderer IPC needed
5. Add handler in `setupMenuEventListeners()` if native menu needs it
6. Add test in `tests/accessibility-contract.test.ts`

## Native Menu Posture

The native Electron menu (`src/main/menu.ts`) is **retained in reduced form**:

- macOS app menu (About, Hide, Quit): kept for platform conformity
- OS keyboard accelerators (Ctrl+Q, Alt+F4): retained for system integration
- Primary workflow commands: **removed** from native menu; served by renderer surface

Rationale:
1. OS keyboard accelerators must work regardless of renderer state
2. macOS standard app menu is required for platform conformity
3. Screen readers that natively interact with OS menu bar still need a fallback
4. Primary workflows benefit from renderer-owned DOM accessibility

## Audit Summary

Changes per the task scope:

### Roles/Names Added
- All interactive buttons: `aria-label`
- Toggle buttons: `aria-pressed`
- Panels/sections: `aria-label`
- Viewport: `role="img"`, `aria-label="3D viewport"`
- Status regions: `role="status"`, `aria-live="polite"`
- Menu bar: `role="menubar"`, `aria-label="Application menu"`
- Footer: `role="contentinfo"`
- Callout info boxes: `role="note"`
- Diagnostic lists: `role="list"`
- HUD overlay: `aria-hidden="true"`

### Focus/Keyboard
- `button:focus-visible` outline style
- `input:focus` outline style
- Skip link for keyboard users
- Full menubar keyboard navigation (Tab, arrows, Enter, Escape, Home, End)

### Stable Test IDs
Not used — all test targeting uses semantic roles, labels, and text content
which are stable across UI refactors. Playwright locators prefer `role=menuitem`
and `text=Reference` over brittle `data-testid` attributes.

### Known Gaps
- No `axe-core` integration yet (Playwright supports it; add via `@axe-core/playwright`)
- `window.prompt` usage: `handleReferenceModelLoad` (Reference > Load Reference Model…)
  uses the async `showRendererPrompt` DOM dialog via `promptUserAsync`; all other
  interactive menu commands (File > Open, Edit > Fill Region, View > Background Color,
  Reference > Transform/Rotate/Scale, etc.) use `window.prompt` synchronously on both
  the native IPC path and the accessible menu dispatch path (both now route through the
  shared `menuCommandHandlers` dispatch table).
- Reference Model Load uses `showRendererPrompt` for the async dialog path (injected via
  `MenuHandlerDeps.promptUserAsync`), falling back to `window.prompt` if unavailable.
- Color contrast not yet audited (theme is dark-on-dark)
- Viewport HUD is `aria-hidden` (decorative overlay)
- No screen reader-specific testing yet
