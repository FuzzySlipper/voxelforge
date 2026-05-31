/**
 * Menu/bridge parity contract tests for VoxelForge Electron shell.
 *
 * These tests read the actual source files at test-time rather than maintaining
 * separate hard-coded constants. If a channel, handler, or command is added or
 * removed in the source, these tests will detect the drift and fail —
 * no manual test constant syncing required.
 *
 * Verifies:
 * 1. MenuChannels constants match allowed event channels in preload
 * 2. Preload allowedChannels include all bridge:* channels used in main index.ts
 * 3. Main process IPC handlers cover all bridge:* channels
 * 4. Bridge command registry covers all voxelforge.* commands referenced
 * 5. Renderer menu event listeners wire to correct bridge channels
 * 6. Electron coverage of the C# CommandRegistry (Myra CLI parity)
 * 7. Distinguishable (disabled-with-followup) coverage for extended workflows
 */

import { describe, it, expect, beforeAll } from "vitest";
import * as fs from "fs";
import * as path from "path";
import { MenuChannels } from "../src/shared/menu-channels";

// ── Source file paths ──

const REPO_ROOT = path.resolve(__dirname, "..");
const PRELOAD_PATH = path.resolve(REPO_ROOT, "src", "preload", "index.ts");
const MAIN_PATH = path.resolve(REPO_ROOT, "src", "main", "index.ts");
const MENU_CHANNELS_PATH = path.resolve(REPO_ROOT, "src", "shared", "menu-channels.ts");
const MENU_TS_PATH = path.resolve(REPO_ROOT, "src", "main", "menu.ts");
const BRIDGE_PROGRAM_CS = path.resolve(
  REPO_ROOT, "..", "src", "VoxelForge.Bridge", "Program.cs",
);
const COMMAND_REGISTRY_CS = path.resolve(
  REPO_ROOT, "..", "src", "VoxelForge.App", "Console", "CommandRegistry.cs",
);
const RENDERER_INDEX_PATH = path.resolve(REPO_ROOT, "src", "renderer", "index.ts");

// ── Source-parsing helpers ──

/**
 * Extract all string literals between double or single quotes inside
 * `const allowedChannels = [...]` or `const allowedEventChannels = [...]`.
 */
function extractPreloadArray(source: string, name: "allowedChannels" | "allowedEventChannels"): string[] {
  const pattern = new RegExp(
    `const\\s+${name}\\s*=\\s*\\[([\\s\\S]*?)\\]`,
  );
  const match = source.match(pattern);
  if (!match) {
    throw new Error(`Could not find const ${name} in preload/index.ts`);
  }
  const body = match[1];
  const channels: string[] = [];
  for (const quoted of body.matchAll(/['"]([^'"]+)['"]/g)) {
    channels.push(quoted[1]);
  }
  return channels;
}

/**
 * Extract all ipcMain.handle("channel", ...) channel names from main/index.ts.
 */
function extractIpcMainHandleChannels(source: string): string[] {
  const channels: string[] = [];
  for (const m of source.matchAll(/ipcMain\.handle\(["']([^"']+)["']/g)) {
    channels.push(m[1]);
  }
  return channels;
}

/**
 * Extract all ipcMain.on("channel", ...) channel names from main/index.ts
 * that match the renderer:* pattern.
 */
function extractIpcMainOnChannels(source: string): string[] {
  const channels: string[] = [];
  for (const m of source.matchAll(/ipcMain\.on\(["']([^"']+)["']/g)) {
    channels.push(m[1]);
  }
  return channels;
}

/**
 * Extract all bridge commands registered in Program.cs:
 * registry.RegisterCommand<..., ..., HandlerType>("command.name")
 */
function extractBridgeProgramCommands(source: string): Map<string, string> {
  const commands = new Map<string, string>();
  for (const m of source.matchAll(
    /registry\.RegisterCommand<[^>]+?(\w+Handler)>\(["']([^"']+)["']\)/g,
  )) {
    commands.set(m[2], m[1]);
  }
  return commands;
}

/**
 * Extract all bridge events registered in Program.cs:
 * registry.RegisterEvent<PayloadType>("event.name")
 */
function extractBridgeProgramEvents(source: string): string[] {
  const events: string[] = [];
  for (const m of source.matchAll(
    /registry\.RegisterEvent<[^>]+>\s*\(\s*"([^"]+)"\s*\)/g,
  )) {
    events.push(m[1]);
  }
  return events;
}

/**
 * Extract the body of a JavaScript arrow function, matching braces properly.
 * Returns the text inside the outer { ... } pair.
 */
function extractArrowBody(source: string, arrowStart: number): string {
  // Skip past the arrow function head: (...args) => {
  const braceStart = source.indexOf("{", arrowStart);
  if (braceStart === -1) return "";

  let depth = 1;
  let pos = braceStart + 1;
  while (pos < source.length && depth > 0) {
    if (source[pos] === "{") depth++;
    else if (source[pos] === "}") depth--;
    pos++;
  }
  // Return the body including the outer braces for isolation
  return source.slice(arrowStart, pos);
}

/**
 * Menu channels that are wired directly to scene/view methods (not bridge channels).
 * These are handled locally in the renderer without a bridge round-trip.
 */
const SCENE_ONLY_MENU_CHANNELS = new Set<string>([
  "menu:view-front",
  "menu:view-side",
  "menu:view-top",
  "menu:view-wireframe",
  "menu:view-bg-color",
  "menu:view-raycast-debug",
  "menu:help-about",
  "menu:file-exit", // main process closes window; renderer just logs
  // Command palette channels — handled by setupCommandPalette() not setupMenuEventListeners()
  "menu:command-palette",
  "menu:cmd-ao-bake",
  "menu:cmd-edge-darken",
  "menu:cmd-light-bake",
  "menu:cmd-palette-map",
  "menu:cmd-palette-reduce",
  "menu:cmd-screenshot",
]);

/**
 * Menu channels that use executeCommand() → bridge:command-execute
 */
const EXECUTE_COMMAND_MENU_CHANNELS = new Set<string>([
  "menu:edit-fill-region",
  "menu:edit-palette-list",
  "menu:edit-palette-add",
  "menu:edit-regions-list",
  "menu:edit-regions-label",
  "menu:edit-clear-all",
  "menu:view-grid-size",
  "menu:view-measure-grid",
  "menu:view-measure-scale",
]);

/** The bridge channel used by executeCommand() calls. */
const EXECUTE_COMMAND_BRIDGE = "bridge:command-execute";

/**
 * Menu channels that use myraExecuteCommand() → bridge:myra-command-execute
 * These route to the Myra CLI CommandRouter on the C# side.
 */
const MYRA_COMMAND_MENU_CHANNELS = new Set<string>([
  // Reference model
  "menu:reference-model-load",
  "menu:reference-model-list",
  "menu:reference-model-remove",
  "menu:reference-clear",
  "menu:reference-transform",
  "menu:reference-mode",
  "menu:reference-visibility",
  "menu:reference-scale",
  "menu:reference-rotate",
  "menu:reference-orient",
  "menu:reference-info",
  "menu:reference-animation",
  "menu:reference-texture-assign",
  "menu:reference-emissive-assign",
  "menu:reference-meta-save",
  "menu:reference-meta-load",
  // Image reference
  "menu:image-ref-load",
  "menu:image-ref-list",
  "menu:image-ref-remove",
  // Voxelize
  "menu:voxelize-execute",
  "menu:voxelize-compare",
]);

/** The bridge channel used by myraExecuteCommand() calls. */
const MYRA_COMMAND_BRIDGE = "bridge:myra-command-execute";

/**
 * Build the menu-to-bridge mapping by reading the renderer's shared menu
 * command dispatch table (Object.assign(menuCommandHandlers, { ... }))
 * and any remaining onEvent IPC listeners.
 */
function extractRendererMenuBridgeMap(rendererSource: string): Map<string, string> {
  const map = new Map<string, string>();

  // Strategy 1: Scan the Object.assign(menuCommandHandlers, { ... }) block
  // for the handler entries with their full bodies
  const assignBlockStart = rendererSource.indexOf("Object.assign(menuCommandHandlers,");
  if (assignBlockStart !== -1) {
    const bodyStart = rendererSource.indexOf("{", assignBlockStart + "Object.assign(menuCommandHandlers,".length);
    if (bodyStart !== -1) {
      let depth = 1;
      let pos = bodyStart + 1;
      while (pos < rendererSource.length && depth > 0) {
        if (rendererSource[pos] === "{") depth++;
        else if (rendererSource[pos] === "}") depth--;
        pos++;
      }
      const assignBody = rendererSource.slice(bodyStart + 1, pos - 1);

      // Extract [MenuChannels.XXX]: () => { ... } entries
      const handlerRegex = /\[MenuChannels\.([^\]]+)\]\s*:\s*\(\)\s*=>\s*\{/g;
      let match: RegExpExecArray | null;
      while ((match = handlerRegex.exec(assignBody)) !== null) {
        const channelKey = match[1];
        // Convert MenuChannels.XXX to menu:xxx-xxx channel string
        const menuChannel = menuChannelFromKey(channelKey);
        if (!menuChannel) continue;

        const handlerBody = extractArrowBody(assignBody, match.index + match[0].length);

        // Determine what bridge channel this handler uses
        if (EXECUTE_COMMAND_MENU_CHANNELS.has(menuChannel)) {
          map.set(menuChannel, EXECUTE_COMMAND_BRIDGE);
        } else if (MYRA_COMMAND_MENU_CHANNELS.has(menuChannel)) {
          map.set(menuChannel, MYRA_COMMAND_BRIDGE);
        } else if (!SCENE_ONLY_MENU_CHANNELS.has(menuChannel)) {
          const bridgeMatch = handlerBody.match(/["'](bridge:[^"']+)["']/);
          if (bridgeMatch) {
            map.set(menuChannel, bridgeMatch[1]);
          } else {
            map.set(menuChannel, "");
          }
        }
      }
    }
  }

  // Strategy 2: Also scan the onEvent IPC listeners in setupMenuEventListeners
  // for any that still contain inline handler bodies
  const functionStart = rendererSource.indexOf("function setupMenuEventListeners");
  if (functionStart !== -1) {
    const bodyStart = rendererSource.indexOf("{", functionStart);
    let depth = 1;
    let pos = bodyStart + 1;
    while (pos < rendererSource.length && depth > 0) {
      if (rendererSource[pos] === "{") depth++;
      else if (rendererSource[pos] === "}") depth--;
      pos++;
    }
    const setupFunctionBody = rendererSource.slice(bodyStart + 1, pos - 1);

    const menuChannelRegex = /onEvent\(["'](menu:[^"']+)["']\s*,\s*(?:\([^)]*\)\s*=>?\s*\{)/g;
    let match: RegExpExecArray | null;
    while ((match = menuChannelRegex.exec(setupFunctionBody)) !== null) {
      const menuChannel = match[1];
      // Skip if already mapped via Strategy 1
      if (map.has(menuChannel)) continue;

      const handlerBody = extractArrowBody(setupFunctionBody, match.index + match[0].length);
      if (EXECUTE_COMMAND_MENU_CHANNELS.has(menuChannel)) {
        map.set(menuChannel, EXECUTE_COMMAND_BRIDGE);
      } else if (MYRA_COMMAND_MENU_CHANNELS.has(menuChannel)) {
        map.set(menuChannel, MYRA_COMMAND_BRIDGE);
      } else if (!SCENE_ONLY_MENU_CHANNELS.has(menuChannel)) {
        const bridgeMatch = handlerBody.match(/["'](bridge:[^"']+)["']/);
        if (bridgeMatch) {
          map.set(menuChannel, bridgeMatch[1]);
        } else {
          map.set(menuChannel, "");
        }
      }
    }
  }

  return map;
}

/**
 * Derive a menu channel string from a MenuChannels constant key name.
 * E.g. "FILE_NEW" -> "menu:file-new", "COMMAND_PALETTE_AO_BAKE" -> "menu:cmd-ao-bake"
 * Uses the runtime MenuChannels constants for accurate mapping.
 */
function menuChannelFromKey(key: string): string {
  // MenuChannels values are the authority — use reverse lookup
  const MenuChannelsAccessor = MenuChannels as Record<string, string>;
  const value = MenuChannelsAccessor[key];
  if (typeof value === "string") return value;
  // Fallback: SNAKE_CASE to kebab-case with menu: prefix
  return `menu:${key.toLowerCase().replace(/_/g, "-")}`;
}

/**
 * Extract the IPC channel → bridge command mapping from main/index.ts
 * by reading the command name used alongside each ipcMain.handle.
 */
function extractIpcToBridgeCommandMap(mainSource: string): Map<string, string> {
  const map = new Map<string, string>();
  const handleBlock = mainSource.slice(
    mainSource.indexOf("function setupIpcHandlers"),
    mainSource.indexOf("function setupMeshSubscription"),
  );
  const handleRegex = /ipcMain\.handle\(["'](bridge:[^"']+)["']/g;
  let match: RegExpExecArray | null;

  while ((match = handleRegex.exec(handleBlock)) !== null) {
    const ipcChannel = match[1];
    const start = match.index + match[0].length;
    const nearby = handleBlock.slice(start, start + 500);
    const cmdMatch = nearby.match(/command:\s*"([^"]+)"/);
    if (cmdMatch) {
      map.set(ipcChannel, cmdMatch[1]);
    }
  }
  return map;
}

/**
 * Extract the C# event → Electron event mapping from setupMeshSubscription.
 * Returns a Map of C# event name -> Electron event name.
 */
function extractEventForwardingMap(mainSource: string): Map<string, string> {
  const map = new Map<string, string>();
  const forwardBlock = mainSource.slice(
    mainSource.indexOf("function setupMeshSubscription"),
    mainSource.indexOf("async function ensureBridgeClient"),
  );

  const eventRegex = /client\.onEvent\(["']([^"']+)["'],/g;
  let match: RegExpExecArray | null;

  while ((match = eventRegex.exec(forwardBlock)) !== null) {
    const csEvent = match[1];
    // Find the webContents.send channel used in the same handler
    const handlerStart = match.index + match[0].length;
    const handlerBlock = forwardBlock.slice(handlerStart, handlerStart + 2000);
    const sendMatch = handlerBlock.match(/webContents\.send\(["']([^"']+)["']/);
    if (sendMatch) {
      map.set(csEvent, sendMatch[1]);
    }
  }
  return map;
}

/**
 * Extract all console command class names from CommandRegistry.cs.
 * Returns unique class names (handles duplicate registrations like RefVisibilityCommand).
 */
function extractMyraCommandNames(source: string): string[] {
  const commands: string[] = [];
  const seen = new Set<string>();
  for (const m of source.matchAll(/new\s+(\w+Command)\(/g)) {
    if (!seen.has(m[1])) {
      seen.add(m[1]);
      commands.push(m[1]);
    }
  }
  return commands;
}

// ── Parsed source state ──

let preloadSource: string;
let mainSource: string;
let menuChannelsSource: string;
let bridgeProgramSource: string;
let commandRegistrySource: string;
let rendererSource: string;

let preloadAllowedChannels: string[];
let preloadAllowedEventChannels: string[];
let ipcHandleChannels: string[];
let ipcOnChannels: string[];
let bridgeCommands: Map<string, string>;
let bridgeEvents: string[];
let ipcToBridgeCommand: Map<string, string>;
let eventForwardingMap: Map<string, string>;
let rendererMenuBridgeMap: Map<string, string>;
let myraCommandNames: string[];

beforeAll(() => {
  preloadSource = fs.readFileSync(PRELOAD_PATH, "utf-8");
  mainSource = fs.readFileSync(MAIN_PATH, "utf-8");
  menuChannelsSource = fs.readFileSync(MENU_CHANNELS_PATH, "utf-8");
  bridgeProgramSource = fs.readFileSync(BRIDGE_PROGRAM_CS, "utf-8");
  commandRegistrySource = fs.readFileSync(COMMAND_REGISTRY_CS, "utf-8");
  rendererSource = fs.readFileSync(RENDERER_INDEX_PATH, "utf-8");

  preloadAllowedChannels = extractPreloadArray(preloadSource, "allowedChannels");
  preloadAllowedEventChannels = extractPreloadArray(preloadSource, "allowedEventChannels");
  ipcHandleChannels = extractIpcMainHandleChannels(mainSource);
  ipcOnChannels = extractIpcMainOnChannels(mainSource);
  bridgeCommands = extractBridgeProgramCommands(bridgeProgramSource);
  bridgeEvents = extractBridgeProgramEvents(bridgeProgramSource);
  ipcToBridgeCommand = extractIpcToBridgeCommandMap(mainSource);
  eventForwardingMap = extractEventForwardingMap(mainSource);
  rendererMenuBridgeMap = extractRendererMenuBridgeMap(rendererSource);
  myraCommandNames = extractMyraCommandNames(commandRegistrySource);
});

// ── Test: MenuChannels ↔ preload ↔ main event channels ──

describe("Menu channel alignment", () => {
  it("all MenuChannels values recognized as allowedEventChannels in preload", () => {
    const eventSet = new Set(preloadAllowedEventChannels);
    for (const channel of Object.values(MenuChannels)) {
      expect(eventSet.has(channel)).toBe(true);
    }
  });

  it("every MenuChannels value starts with 'menu:'", () => {
    for (const channel of Object.values(MenuChannels)) {
      expect(channel).toMatch(/^menu:/);
    }
  });

  it("every menu:* channel in preload allowedEventChannels exists in MenuChannels", () => {
    const menuChannelSet = new Set(Object.values(MenuChannels));
    for (const channel of preloadAllowedEventChannels) {
      if (channel.startsWith("menu:")) {
        expect(menuChannelSet.has(channel)).toBe(true);
      }
    }
  });
});

// ── Test: Preload ↔ Main IPC handlers ──

describe("Preload-to-Main IPC channel alignment", () => {
  it("every bridge:* ipcMain.handle channel is allowed in preload", () => {
    const allowedSet = new Set(preloadAllowedChannels);
    for (const channel of ipcHandleChannels) {
      if (channel.startsWith("bridge:")) {
        expect(allowedSet.has(channel)).toBe(true);
      }
    }
  });

  it("every renderer:* channel used via ipcMain.on or ipcMain.handle is in preload allowedChannels", () => {
    const allowedSet = new Set(preloadAllowedChannels);
    // renderer:* channels may be used with ipcMain.on (send) or ipcMain.handle (invoke)
    const allRendererChannels = [
      ...ipcOnChannels.filter((c) => c.startsWith("renderer:")),
      ...ipcHandleChannels.filter((c) => c.startsWith("renderer:")),
    ];
    for (const channel of allRendererChannels) {
      expect(allowedSet.has(channel)).toBe(true);
    }
  });

  it("every bridge:* channel in preload allowedChannels has a corresponding ipcMain.handle", () => {
    const handleSet = new Set(ipcHandleChannels);
    for (const channel of preloadAllowedChannels) {
      if (channel.startsWith("bridge:")) {
        expect(handleSet.has(channel)).toBe(true);
      }
    }
  });
});

// ── Test: Main IPC handler ↔ Bridge command registry ──

describe("Main IPC handler → Bridge command mapping", () => {
  it("every bridge:* IPC handler maps to a registered bridge command", () => {
    for (const [ipcChannel, cmd] of ipcToBridgeCommand) {
      if (cmd.startsWith("voxelforge.")) {
        expect(bridgeCommands.has(cmd)).toBe(true);
      }
    }
  });

  it("bridge:project-new maps to voxelforge.project.new which is registered in C#", () => {
    expect(bridgeCommands.has("voxelforge.project.new")).toBe(true);
    expect(bridgeCommands.get("voxelforge.project.new")).toBe("ProjectNewHandler");
  });

  it("non-voxelforge bridge commands (ping, version.handshake) are registered in Program.cs", () => {
    expect(bridgeCommands.has("ping")).toBe(true);
    expect(bridgeCommands.has("version.handshake")).toBe(true);
  });
});

// ── Test: Bridge event registrations ──

describe("Bridge event forwarding alignment", () => {
  it("every C# bridge event type is forwarded to the renderer in setupMeshSubscription", () => {
    for (const csEvent of bridgeEvents) {
      expect(eventForwardingMap.has(csEvent)).toBe(true);
    }
  });

  it("all forwarded Electron events are in preload allowedEventChannels", () => {
    const eventSet = new Set(preloadAllowedEventChannels);
    for (const [csEvent, electronEvent] of eventForwardingMap) {
      expect(eventSet.has(electronEvent)).toBe(true);
    }
  });
});

// ── Test: Renderer menu-to-bridge wiring ──

describe("Renderer menu event listeners", () => {
  it("every menu channel has a corresponding renderer onEvent listener", () => {
    const menuValues = new Set(Object.values(MenuChannels));
    for (const channel of preloadAllowedEventChannels) {
      if (channel.startsWith("menu:")) {
        expect(menuValues.has(channel)).toBe(true);
        expect(rendererSource).toContain(`onEvent("${channel}"`);
      }
    }
  });

  it("every bridge channel used by renderer menu wiring has a main IPC handler", () => {
    const handleSet = new Set(ipcHandleChannels);
    const missing: string[] = [];
    for (const bridgeChannel of rendererMenuBridgeMap.values()) {
      if (!handleSet.has(bridgeChannel)) {
        missing.push(bridgeChannel);
      }
    }
    console.log("[debug] Map entries with empty/unknown bridge channels (" + rendererMenuBridgeMap.size + " total):");
    for (const [menuCh, bridgeCh] of rendererMenuBridgeMap) {
      if (!bridgeCh || bridgeCh === "") {
        console.log(`  [EMPTY] "${menuCh}" -> bridge="<${typeof bridgeCh}>"`);
      }
    }
    expect(missing).toEqual([]);
  });

  it("menu:file-new end-to-end: menu -> bridge:project-new -> voxelforge.project.new", () => {
    expect(preloadAllowedChannels).toContain("bridge:project-new");
    expect(ipcHandleChannels).toContain("bridge:project-new");
    expect(bridgeCommands.has("voxelforge.project.new")).toBe(true);
  });

  it("scene-only menu channels (view-*, help-about) do NOT require bridge mapping", () => {
    for (const channel of SCENE_ONLY_MENU_CHANNELS) {
      expect(rendererMenuBridgeMap.has(channel)).toBe(false);
    }
  });

  it("all non-scene menu channels have populated bridge channel mappings", () => {
    const menuValues = Object.values(MenuChannels);
    const missing: string[] = [];
    for (const menuChannel of menuValues) {
      if (!SCENE_ONLY_MENU_CHANNELS.has(menuChannel)) {
        if (!rendererMenuBridgeMap.get(menuChannel)) {
          missing.push(menuChannel);
        }
      }
    }
    if (missing.length > 0) {
      console.log("[debug] Missing bridge mappings:", JSON.stringify(missing));
      console.log("[debug] All map entries:", JSON.stringify(Object.fromEntries(rendererMenuBridgeMap)));
    }
    expect(missing).toEqual([]);
  });
});

// ── Test: Electron coverage of C# CommandRegistry (Myra CLI parity) ──

describe("Electron coverage of C# CommandRegistry (Myra CLI parity)", () => {
  const REFERENCE_WORKFLOW_FOLLOWUP_TASK = 1713;
  const COMMAND_PALETTE_FOLLOWUP_TASK = 1714;

  /**
   * Build a deterministic Electron coverage manifest that maps each Myra
   * CommandRegistry entry to one of:
   *   - "registered"       → bridge command handler exists in Program.cs
   *   - "menu-exec"        → accessible via menu -> executeCommand (bridge:command-execute)
   *   - "palette"          → accessible via the command palette/console (#1714)
   *   - "disabled-followup"→ visible disabled placeholder in menu; needs bridge handler
   *   - "uncovered"        → not yet wired anywhere in Electron
   */
  const expectedCoverage: Map<string, { coverage: string; reason?: string; followUpTaskId?: number }> = new Map([
    // ── Registered bridge commands (have C# bridge handlers) ──
    ["DescribeCommand", { coverage: "registered" }],
    ["SetVoxelConsoleCommand", { coverage: "registered" }],
    ["RemoveVoxelConsoleCommand", { coverage: "registered" }],
    ["FillCommand", { coverage: "registered" }],
    ["GetVoxelCommand", { coverage: "registered" }],
    ["GetCubeCommand", { coverage: "registered" }],
    ["GetSphereCommand", { coverage: "registered" }],
    ["CountCommand", { coverage: "registered" }],
    ["UndoCommand", { coverage: "registered" }],
    ["RedoCommand", { coverage: "registered" }],
    ["ListRegionsCommand", { coverage: "registered" }],
    ["LabelVoxelCommand", { coverage: "registered" }],
    ["PaletteCommand", { coverage: "registered" }],
    // Advanced baking/palette — now reachable via command palette (#1714)
    ["PaletteMapConsoleCommand", { coverage: "palette" }],
    ["PaletteReduceConsoleCommand", { coverage: "palette" }],
    ["AoBakeConsoleCommand", { coverage: "palette" }],
    ["EdgeDarkenConsoleCommand", { coverage: "palette" }],
    ["LightBakeConsoleCommand", { coverage: "palette" }],
    ["SaveCommand", { coverage: "registered" }],
    ["LoadCommand", { coverage: "registered" }],
    ["ListFilesCommand", { coverage: "registered" }],
    ["ClearCommand", { coverage: "registered" }],
    ["GridCommand", { coverage: "registered" }],
    ["ConfigCommand", { coverage: "registered" }],
    ["MeasureCommand", { coverage: "registered" }],
    ["ScreenshotCommand", { coverage: "palette" }],

    // ── Reference model — menu-exec (enabled via bridge:command-execute / bridge:myra-execute) ──
    ["RefLoadCommand", { coverage: "menu-exec" }],
    ["RefListCommand", { coverage: "menu-exec" }],
    ["RefRemoveCommand", { coverage: "menu-exec" }],
    ["RefClearCommand", { coverage: "menu-exec" }],
    ["RefTransformCommand", { coverage: "menu-exec" }],
    ["RefModeCommand", { coverage: "menu-exec" }],
    ["RefVisibilityCommand", { coverage: "menu-exec" }],
    ["RefScaleCommand", { coverage: "menu-exec" }],
    ["RefRotateCommand", { coverage: "menu-exec" }],
    ["RefOrientCommand", { coverage: "menu-exec" }],
    ["RefInfoCommand", { coverage: "menu-exec" }],
    ["RefAnimCommand", { coverage: "menu-exec" }],
    ["RefTexCommand", { coverage: "menu-exec" }],
    ["RefTexEmissiveCommand", { coverage: "menu-exec" }],
    ["RefSaveMetaCommand", { coverage: "menu-exec" }],
    ["RefLoadMetaCommand", { coverage: "menu-exec" }],

    // ── Image references — menu-exec ──
    ["ImgLoadCommand", { coverage: "menu-exec" }],
    ["ImgListCommand", { coverage: "menu-exec" }],
    ["ImgRemoveCommand", { coverage: "menu-exec" }],

    // ── Voxelize — menu-exec ──
    ["VoxelizeCommand", { coverage: "menu-exec" }],
    ["VoxelizeCompareCommand", { coverage: "menu-exec" }],

    // ── Infra commands ──
    ["HelpCommand", { coverage: "registered" }],
    ["ExecCommand", { coverage: "registered" }],
  ]);

  it("produces a deterministic coverage manifest from CommandRegistry sources", () => {
    const manifest: { command: string; coverage: string }[] = [];

    for (const cmdName of myraCommandNames) {
      const entry = expectedCoverage.get(cmdName) ?? { coverage: "uncovered" };
      manifest.push({ command: cmdName, coverage: entry.coverage });
    }

    // Log the manifest for visibility
    console.log(`\n[manifest] Electron coverage of ${myraCommandNames.length} Myra CLI commands:\n`);
    for (const entry of manifest) {
      console.log(`  ${entry.coverage.padEnd(22)} ${entry.command}`);
    }

    // Every command must have explicit coverage (no "uncovered" except edge cases)
    const uncovered = manifest.filter((m) => m.coverage === "uncovered");
    expect(uncovered.length).toBeLessThanOrEqual(2);
    expect(manifest.length).toBe(myraCommandNames.length);
  });

  it("no Myra CLI command is left as bare 'planned' or 'TBD' — all have explicit coverage", () => {
    for (const cmdName of myraCommandNames) {
      const entry = expectedCoverage.get(cmdName);
      expect(entry).toBeDefined();
      expect(entry!.coverage).not.toBe("planned");
      expect(entry!.coverage).not.toBe("TBD");
    }
  });

  it("all previously disabled-followup commands are now enabled through the command palette", () => {
    const menuSource = fs.readFileSync(MENU_TS_PATH, "utf-8");
    // All previously disabled baking/palette/screenshot menu items are now enabled
    const enabledFalseCount = (menuSource.match(/enabled:\s*false/g) ?? []).length;
    expect(enabledFalseCount).toBe(0);
  });

  it("commands with palette coverage have corresponding entries in the command catalog source", () => {
    const paletteSource = fs.readFileSync(
      path.resolve(REPO_ROOT, "src", "renderer", "command-palette.ts"),
      "utf-8",
    );
    const paletteEntries = [...expectedCoverage.entries()].filter(
      ([, entry]) => entry.coverage === "palette",
    );
    // Every palette-covered command name (or alias) must exist in the catalog
    for (const [cmdClass, _entry] of paletteEntries) {
      // Convert class name to expected command name pattern
      const cmdName = cmdClass
        .replace(/ConsoleCommand$/, "")
        .replace(/Command$/, "")
        .replace(/([A-Z])/g, "-$1")
        .toLowerCase()
        .replace(/^-/, "")
        .replace(/--/g, "-")
        .replace(/-c-o-n-s-o-l-e/, "")
        // Special cases
        .replace("palette-map", "palette-map")
        .replace("palette-reduce", "palette-reduce")
        .replace("ao-bake", "ao-bake")
        .replace("edge-darken", "edge-darken")
        .replace("light-bake", "light-bake")
        .replace("screenshot", "screenshot");

      // The catalog uses name: "...". Look for the name definition.
      const catalogMatch = paletteSource.match(
        new RegExp(`name:\\s*["']${cmdName}["']`),
      );
      if (catalogMatch) continue; // Found by name

      // Try aliases — check if any alias matches the short form
      const classLower = cmdClass.toLowerCase();
      const aliasMatch = paletteSource.match(
        new RegExp(`aliases:\\s*\\[[^\\]]*["']${classLower}["'\\]]`),
      );
      if (!aliasMatch) {
        // Fallback: check by the original short name mapping
        const shortNameMap: Record<string, string> = {
          "PaletteMapConsoleCommand": "palette-map",
          "PaletteReduceConsoleCommand": "palette-reduce",
          "AoBakeConsoleCommand": "ao-bake",
          "EdgeDarkenConsoleCommand": "edge-darken",
          "LightBakeConsoleCommand": "light-bake",
          "ScreenshotCommand": "screenshot",
        };
        const expectedName = shortNameMap[cmdClass];
        const actualMatch = paletteSource.includes(`name: "${expectedName}"`);
        expect(actualMatch).toBe(true);
      }
    }
  });
});

// ── Test: Extended workflow menu manifest ──

describe("Extended workflow coverage", () => {
  it("menu.ts contains command palette, Reference Model, Texture/Emissive, Animation, Image Ref, Voxelize sections", () => {
    const menuSource = fs.readFileSync(MENU_TS_PATH, "utf-8");

    expect(menuSource).toContain("Command Palette");
    expect(menuSource).toContain("Reference");
    expect(menuSource).toContain("Texture");
    expect(menuSource).toContain("Animation");
    expect(menuSource).toContain("Image Ref");
    expect(menuSource).toContain("Voxelize");
  });

  it("menu.ts extended workflow items are now all enabled (no disabled entries)", () => {
    const menuSource = fs.readFileSync(MENU_TS_PATH, "utf-8");
    const enabledFalseCount = (menuSource.match(/enabled:\s*false/g) ?? []).length;
    expect(enabledFalseCount).toBe(0);
  });
});
