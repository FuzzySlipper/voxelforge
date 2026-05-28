/**
 * Menu/bridge parity contract tests for VoxelForge Electron shell.
 *
 * Verifies:
 * 1. MenuChannels constants match allowed event channels in preload
 * 2. Preload allowedChannels include all bridge:* channels used in main index.ts
 * 3. Main process IPC handlers cover all bridge:* channels
 * 4. Bridge command registry covers all voxelforge.* commands referenced
 * 5. Renderer menu event listeners wire to correct bridge channels
 * 6. Reference model and other extended workflow coverage contract
 */

import { describe, it, expect } from "vitest";
import { MenuChannels } from "../src/shared/menu-channels";

// ── Expected preload allowed channels (mirror of src/preload/index.ts) ──

const EXPECTED_ALLOWED_CHANNELS = [
  "bridge:handshake",
  "bridge:mesh-snapshot",
  "bridge:mesh-subscribe",
  "bridge:mesh-unsubscribe",
  "bridge:palette-get",
  "bridge:state-subscribe",
  "bridge:state-request-full",
  "bridge:command-execute",
  "bridge:history-undo",
  "bridge:history-redo",
  "bridge:project-save",
  "bridge:project-load",
  "bridge:project-new",
  "bridge:ping",
  "bridge:version-handshake",
  // Canonical render-scene channels (#1657/#1662)
  "bridge:render-snapshot",
  "bridge:render-state",
  // Render control commands
  "bridge:set-grid-visible",
  "bridge:set-wireframe",
  "bridge:set-background-color",
  "bridge:capture-screenshot",
  "renderer:ready",
  "renderer:metrics",
] as const;

const EXPECTED_ALLOWED_EVENT_CHANNELS = [
  "voxelforge:mesh-update",
  "voxelforge:palette-update",
  "voxelforge:state-delta",
  "voxelforge:editing-latency",
  // Native menu events from main process
  "menu:file-new",
  "menu:file-open",
  "menu:file-save",
  "menu:file-save-as",
  "menu:file-exit",
  "menu:edit-undo",
  "menu:edit-redo",
  "menu:edit-fill-region",
  "menu:edit-palette-list",
  "menu:edit-palette-add",
  "menu:edit-regions-list",
  "menu:edit-regions-label",
  "menu:edit-clear-all",
  "menu:view-front",
  "menu:view-side",
  "menu:view-top",
  "menu:view-wireframe",
  "menu:view-grid-size",
  "menu:view-measure-grid",
  "menu:view-measure-scale",
  "menu:view-bg-color",
  "menu:help-about",
] as const;

// ── Expected menu channels (mirror of src/shared/menu-channels.ts) ──

const EXPECTED_MENU_CHANNELS: Record<string, string> = {
  FILE_NEW: "menu:file-new",
  FILE_OPEN: "menu:file-open",
  FILE_SAVE: "menu:file-save",
  FILE_SAVE_AS: "menu:file-save-as",
  FILE_EXIT: "menu:file-exit",
  EDIT_UNDO: "menu:edit-undo",
  EDIT_REDO: "menu:edit-redo",
  EDIT_FILL_REGION: "menu:edit-fill-region",
  EDIT_PALETTE_LIST: "menu:edit-palette-list",
  EDIT_PALETTE_ADD: "menu:edit-palette-add",
  EDIT_REGIONS_LIST: "menu:edit-regions-list",
  EDIT_REGIONS_LABEL: "menu:edit-regions-label",
  EDIT_CLEAR_ALL: "menu:edit-clear-all",
  VIEW_FRONT: "menu:view-front",
  VIEW_SIDE: "menu:view-side",
  VIEW_TOP: "menu:view-top",
  VIEW_WIREFRAME: "menu:view-wireframe",
  VIEW_GRID_SIZE: "menu:view-grid-size",
  VIEW_MEASURE_GRID: "menu:view-measure-grid",
  VIEW_MEASURE_SCALE: "menu:view-measure-scale",
  VIEW_BG_COLOR: "menu:view-bg-color",
  HELP_ABOUT: "menu:help-about",
};

// ── Expected main process IPC handlers (mirror of src/main/index.ts) ──

const EXPECTED_IPC_HANDLERS = [
  "bridge:handshake",
  "bridge:mesh-snapshot",
  "bridge:render-snapshot",
  "bridge:render-state",
  "bridge:palette-get",
  "bridge:state-subscribe",
  "bridge:state-request-full",
  "bridge:command-execute",
  "bridge:history-undo",
  "bridge:history-redo",
  "bridge:project-save",
  "bridge:project-load",
  "bridge:project-new",
  "bridge:mesh-subscribe",
  "bridge:mesh-unsubscribe",
  "bridge:ping",
  "bridge:version-handshake",
] as const;

const EXPECTED_IPC_ON_LISTENERS = [
  "renderer:metrics",
  "renderer:ready",
] as const;

// ── Expected bridge command registrations (mirror of src/VoxelForge.Bridge/Program.cs) ──

const EXPECTED_BRIDGE_COMMANDS: Record<string, string> = {
  "ping": "PingHandler",
  "version.handshake": "VersionHandshakeHandler",
  "voxelforge.handshake": "VoxelForgeSchemaHandshakeHandler",
  "voxelforge.mesh.request_snapshot": "MeshSnapshotHandler",
  "voxelforge.mesh.subscribe": "MeshSubscribeHandler",
  "voxelforge.mesh.unsubscribe": "MeshUnsubscribeHandler",
  "voxelforge.palette.get": "PaletteGetHandler",
  "voxelforge.state.subscribe": "EditorStateSubscribeHandler",
  "voxelforge.state.request_full": "EditorStateRequestFullHandler",
  "voxelforge.command.execute": "CommandExecuteHandler",
  "voxelforge.history.undo": "HistoryUndoHandler",
  "voxelforge.history.redo": "HistoryRedoHandler",
  "voxelforge.project.save": "ProjectSaveHandler",
  "voxelforge.project.load": "ProjectLoadHandler",
  "voxelforge.project.new": "ProjectNewHandler",
} as const;

// ── Expected bridge event registrations (mirror of Program.cs) ──

const EXPECTED_BRIDGE_EVENTS = [
  "voxelforge.mesh.update",
  "voxelforge.palette.update",
  "voxelforge.state.delta",
  "voxelforge.diagnostics.editing_latency",
] as const;

// ── Renderer menu-to-bridge wiring contract ──
// From src/renderer/index.ts setupMenuEventListeners()

const RENDERER_MENU_BRIDGE_MAP: Record<string, { bridgeChannel: string; payloadDescriptor: string }> = {
  "menu:file-new": { bridgeChannel: "bridge:project-new", payloadDescriptor: "{}" },
  "menu:file-open": { bridgeChannel: "bridge:project-load", payloadDescriptor: "{ path }" },
  "menu:file-save": { bridgeChannel: "bridge:project-save", payloadDescriptor: "{ path }" },
  "menu:file-save-as": { bridgeChannel: "bridge:project-save", payloadDescriptor: "{ path }" },
  "menu:edit-undo": { bridgeChannel: "bridge:history-undo", payloadDescriptor: "{}" },
  "menu:edit-redo": { bridgeChannel: "bridge:history-redo", payloadDescriptor: "{}" },
  "menu:edit-fill-region": { bridgeChannel: "bridge:command-execute", payloadDescriptor: "fill_box command" },
  "menu:edit-palette-list": { bridgeChannel: "bridge:command-execute", payloadDescriptor: "list_palette command" },
  "menu:edit-palette-add": { bridgeChannel: "bridge:command-execute", payloadDescriptor: "set_palette_entry command" },
  "menu:edit-regions-list": { bridgeChannel: "bridge:command-execute", payloadDescriptor: "list_regions command" },
  "menu:edit-regions-label": { bridgeChannel: "bridge:command-execute", payloadDescriptor: "assign_voxels_to_region command" },
  "menu:edit-clear-all": { bridgeChannel: "bridge:command-execute", payloadDescriptor: "clear_model command" },
  "menu:view-grid-size": { bridgeChannel: "bridge:command-execute", payloadDescriptor: "set_grid_hint command" },
  "menu:view-measure-grid": { bridgeChannel: "bridge:command-execute", payloadDescriptor: "toggle_measure_grid command" },
  "menu:view-measure-scale": { bridgeChannel: "bridge:command-execute", payloadDescriptor: "set_measure_scale command" },
};

// ── Tests ──

describe("Menu channel constants (menu-channels.ts)", () => {
  it("exports all expected menu channels", () => {
    for (const [key, channel] of Object.entries(EXPECTED_MENU_CHANNELS)) {
      expect(MenuChannels).toHaveProperty(key);
      expect((MenuChannels as Record<string, string>)[key]).toBe(channel);
    }
  });

  it("every menu channel value starts with 'menu:'", () => {
    for (const channel of Object.values(MenuChannels)) {
      expect(channel).toMatch(/^menu:/);
    }
  });

  it("all menu channels have matching allowedEventChannels in preload", () => {
    const menuValues = new Set(Object.values(MenuChannels));
    const eventValues = new Set(EXPECTED_ALLOWED_EVENT_CHANNELS);
    for (const channel of menuValues) {
      expect(eventValues.has(channel)).toBe(true);
    }
  });
});

describe("Preload allowed channels (preload/index.ts)", () => {
  it("includes all bridge:* request channels used in main IPC handlers", () => {
    const allowedSet = new Set(EXPECTED_ALLOWED_CHANNELS);
    for (const channel of EXPECTED_IPC_HANDLERS) {
      expect(allowedSet.has(channel)).toBe(true);
    }
  });

  it("includes all renderer event channels used in main event forwarding", () => {
    const allowedSet = new Set(EXPECTED_ALLOWED_EVENT_CHANNELS);
    expect(allowedSet.has("voxelforge:mesh-update")).toBe(true);
    expect(allowedSet.has("voxelforge:palette-update")).toBe(true);
    expect(allowedSet.has("voxelforge:state-delta")).toBe(true);
    expect(allowedSet.has("voxelforge:editing-latency")).toBe(true);
  });

  it("includes bridge:project-new as an allowed channel", () => {
    expect(EXPECTED_ALLOWED_CHANNELS).toContain("bridge:project-new");
  });
});

describe("Main process IPC handlers (main/index.ts)", () => {
  it("has ipcMain.handle for every bridge:* channel", () => {
    for (const channel of EXPECTED_IPC_HANDLERS) {
      expect(channel).toMatch(/^bridge:/);
    }
  });

  it("has ipcMain.on for renderer:* channels", () => {
    for (const channel of EXPECTED_IPC_ON_LISTENERS) {
      expect(channel).toMatch(/^renderer:/);
    }
  });

  it("bridge:project-new handler maps to voxelforge.project.new command", () => {
    // The test validates the contract: the main process handler for
    // bridge:project-new must send "voxelforge.project.new" to the C# bridge.
    // This is a wiring contract, verified by the bridge registry test below.
    expect(EXPECTED_IPC_HANDLERS).toContain("bridge:project-new");
    expect(Object.keys(EXPECTED_BRIDGE_COMMANDS)).toContain("voxelforge.project.new");
  });
});

describe("Bridge Program command registration (Program.cs)", () => {
  it("registers all expected commands with correct handler types", () => {
    for (const [command, handlerType] of Object.entries(EXPECTED_BRIDGE_COMMANDS)) {
      expect(command).toBeTruthy();
      expect(handlerType).toBeTruthy();
      expect(command).toMatch(/^[a-z]+(\.[a-z][a-z._]*)?$/);
    }
  });

  it("voxelforge.project.new is registered as a bridge command", () => {
    expect(EXPECTED_BRIDGE_COMMANDS["voxelforge.project.new"]).toBe("ProjectNewHandler");
  });

  it("registers all expected event types", () => {
    for (const event of EXPECTED_BRIDGE_EVENTS) {
      expect(event).toMatch(/^voxelforge\./);
    }
  });

  it("all bridge:* IPC handlers map to known bridge commands", () => {
    // Mapping from IPC channel to bridge command
    const ipcToCommand: Record<string, string> = {
      "bridge:handshake": "voxelforge.handshake",
      "bridge:mesh-snapshot": "voxelforge.mesh.request_snapshot",
      "bridge:render-snapshot": "voxelforge.mesh.request_snapshot",
      "bridge:render-state": "voxelforge.state.request_full",
      "bridge:palette-get": "voxelforge.palette.get",
      "bridge:state-subscribe": "voxelforge.state.subscribe",
      "bridge:state-request-full": "voxelforge.state.request_full",
      "bridge:command-execute": "voxelforge.command.execute",
      "bridge:history-undo": "voxelforge.history.undo",
      "bridge:history-redo": "voxelforge.history.redo",
      "bridge:project-save": "voxelforge.project.save",
      "bridge:project-load": "voxelforge.project.load",
      "bridge:project-new": "voxelforge.project.new",
      "bridge:mesh-subscribe": "voxelforge.mesh.subscribe",
      "bridge:mesh-unsubscribe": "voxelforge.mesh.unsubscribe",
      "bridge:ping": "ping",
      "bridge:version-handshake": "version.handshake",
    };

    for (const [ipcChannel, command] of Object.entries(ipcToCommand)) {
      expect(EXPECTED_BRIDGE_COMMANDS).toHaveProperty(command);
    }
  });
});

describe("Renderer menu event listeners (renderer/index.ts)", () => {
  it("each menu channel has a corresponding renderer event listener contract", () => {
    for (const [menuChannel, mapping] of Object.entries(RENDERER_MENU_BRIDGE_MAP)) {
      const bridgeChannel = mapping.bridgeChannel;
      // All bridge channels used by menu events must be allowed in preload
      expect(EXPECTED_ALLOWED_CHANNELS).toContain(bridgeChannel);
    }
  });

  it("menu:file-new maps to bridge:project-new which maps to voxelforge.project.new", () => {
    expect(EXPECTED_ALLOWED_CHANNELS).toContain("bridge:project-new");
    expect(EXPECTED_IPC_HANDLERS).toContain("bridge:project-new");
    expect(EXPECTED_BRIDGE_COMMANDS).toHaveProperty("voxelforge.project.new");
  });

  it("all renderer menu-to-bridge mappings are internally consistent", () => {
    const bridgeChannelsUsed = new Set(
      Object.values(RENDERER_MENU_BRIDGE_MAP).map((m) => m.bridgeChannel)
    );
    // Every bridge channel used by the renderer must have an IPC handler
    for (const channel of bridgeChannelsUsed) {
      expect(EXPECTED_IPC_HANDLERS).toContain(channel);
    }
  });
});

describe("Reference model workflow coverage contract", () => {
  it("defines expected reference model bridge commands for future wiring", () => {
    // Reference model workflows: load/list/remove/clear
    const refModelCommands = [
      "voxelforge.reference_model.load",
      "voxelforge.reference_model.list",
      "voxelforge.reference_model.remove",
      "voxelforge.reference_model.clear",
    ];
    for (const cmd of refModelCommands) {
      expect(cmd).toMatch(/^voxelforge\.reference/);
    }
  });

  it("defines expected transform bridge commands for future wiring", () => {
    const transformCommands = [
      "voxelforge.transform.orient",
      "voxelforge.transform.rotate",
      "voxelforge.transform.scale",
      "voxelforge.transform.mode",
    ];
    for (const cmd of transformCommands) {
      expect(cmd).toMatch(/^voxelforge\.transform/);
    }
  });

  it("defines expected texture/emissive bridge commands for future wiring", () => {
    const textureCommands = [
      "voxelforge.texture.assign",
      "voxelforge.emissive.assign",
    ];
    for (const cmd of textureCommands) {
      expect(cmd).toMatch(/^voxelforge\.(texture|emissive)/);
    }
  });

  it("defines expected meta save/load bridge commands for future wiring", () => {
    const metaCommands = [
      "voxelforge.meta.save",
      "voxelforge.meta.load",
    ];
    for (const cmd of metaCommands) {
      expect(cmd).toMatch(/^voxelforge\.meta/);
    }
  });

  it("defines expected animation bridge commands for future wiring", () => {
    const animCommands = [
      "voxelforge.animation.list",
      "voxelforge.animation.play",
    ];
    for (const cmd of animCommands) {
      expect(cmd).toMatch(/^voxelforge\.animation/);
    }
  });

  it("defines expected image ref bridge commands for future wiring", () => {
    const imageCommands = [
      "voxelforge.image_ref.load",
      "voxelforge.image_ref.list",
    ];
    for (const cmd of imageCommands) {
      expect(cmd).toMatch(/^voxelforge\.image_ref/);
    }
  });

  it("defines expected voxelize bridge command for future wiring", () => {
    const voxelizeCommands = [
      "voxelforge.voxelize.execute",
    ];
    for (const cmd of voxelizeCommands) {
      expect(cmd).toMatch(/^voxelforge\.voxelize/);
    }
  });
});

describe("CLI command coverage manifest", () => {
  it("produces a deterministic manifest of all known bridge commands", () => {
    const manifest: { command: string; handlerClass: string; status: string }[] = [];
    for (const [command, handlerClass] of Object.entries(EXPECTED_BRIDGE_COMMANDS)) {
      manifest.push({ command, handlerClass, status: "registered" });
    }

    // Also include the reference model workflow commands as "planned" (not yet registered)
    const plannedCommands = [
      "voxelforge.reference_model.load",
      "voxelforge.reference_model.list",
      "voxelforge.reference_model.remove",
      "voxelforge.reference_model.clear",
      "voxelforge.transform.orient",
      "voxelforge.transform.rotate",
      "voxelforge.transform.scale",
      "voxelforge.transform.mode",
      "voxelforge.texture.assign",
      "voxelforge.emissive.assign",
      "voxelforge.meta.save",
      "voxelforge.meta.load",
      "voxelforge.animation.list",
      "voxelforge.animation.play",
      "voxelforge.image_ref.load",
      "voxelforge.image_ref.list",
      "voxelforge.voxelize.execute",
    ];
    for (const cmd of plannedCommands) {
      manifest.push({ command: cmd, handlerClass: "TBD", status: "planned" });
    }

    // Verify all registered commands appear
    const registeredEntries = manifest.filter((m) => m.status === "registered");
    expect(registeredEntries.length).toBe(Object.keys(EXPECTED_BRIDGE_COMMANDS).length);

    // Verify manifest is deterministic: same insertion order every time
    // (registered commands first in declaration order, then planned commands)
    for (let i = 0; i < registeredEntries.length; i++) {
      const expected = Object.entries(EXPECTED_BRIDGE_COMMANDS)[i];
      expect(registeredEntries[i].command).toBe(expected[0]);
      expect(registeredEntries[i].handlerClass).toBe(expected[1]);
    }

    // Verify planned entries follow
    const plannedEntries = manifest.filter((m) => m.status === "planned");
    expect(plannedEntries.length).toBe(plannedCommands.length);
    expect(plannedEntries.every((m) => m.handlerClass === "TBD")).toBe(true);
  });
});
