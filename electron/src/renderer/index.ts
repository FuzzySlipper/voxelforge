/**
 * Electron renderer process entry point for the VoxelForge workbench.
 * Owns presentation, camera, and UI rendering only. All semantic state changes
 * route through Electron main -> den-bridge -> C# sidecar commands/state.
 */

import { VoxelForgeScene, type RendererMetrics } from "../renderer-core";
import type { RenderSceneSnapshot, RenderStateSummary, TransitionalMeshSnapshot } from "../renderer-core/protocol/types";
import { transitionalMeshToSnapshot } from "../renderer-core/protocol/normalizeSnapshot";
// Backward-compat types from scene.ts shim
import type { MeshSnapshotData, MeshUpdateEventData, PaletteUpdateEventData } from "./scene";
import { titleCase, formatError, escapeHtml } from "../shared/string-utils";
import { createCoalescer } from "../shared/refresh-coalescer";
import { CommandPalette } from "./command-palette-ui";
import { createRendererDeps } from "./menu-handler-deps";
import {
  handleReferenceModelLoad,
  handleFileOpen,
  handleFileSaveAs,
  handleReferenceMetaLoad,
  handleReferenceMetaSave,
  handleImageRefLoad,
  handleReferenceTextureAssign,
  handleReferenceEmissiveAssign,
} from "./menu-handlers";
import { AccessibleMenuSurface } from "./accessible-menu-surface";
import { MenuChannels } from "../shared/menu-channels";
import { menuCommandHandlers } from "../shared/menu-command-dispatch";
import {
  computeScreenToNDC,
  buildRaycastDebugEvent,
  buildRaycastMissDebugEvent,
} from "../renderer-core/scene/RaycastDebugger";

declare global {
  interface Window {
    voxelforgeBridge: {
      request(channel: string, payload: unknown): Promise<unknown>;
      onEvent(channel: string, callback: (payload: unknown) => void): () => void;
      notifyReady(): void;
      sendMetrics(metrics: Record<string, number>): void;
      selectFile(request: import("../shared/dialog-types").DialogRequest): Promise<import("../shared/dialog-types").DialogResponse>;
      saveFile(request: import("../shared/dialog-types").DialogRequest): Promise<import("../shared/dialog-types").DialogResponse>;
    };
  }
}

interface EditorUiStateSnapshot {
  model_id: string;
  project_path?: string;
  is_dirty: boolean;
  voxel_count: number;
  bounds?: { min_x: number; min_y: number; min_z: number; max_x: number; max_y: number; max_z: number };
  grid_hint: number;
  active_tool: string;
  active_palette_index: number;
  available_tools: string[];
  palette_entries: PaletteEntry[];
  palette_entry_count: number;
  can_undo: boolean;
  can_redo: boolean;
  undo_depth: number;
  redo_depth: number;
  last_command_description?: string;
  selected_voxel_count: number;
  active_frame_index: number;
  status_message: string;
  timestamp: string;
}

interface PaletteEntry {
  index: number;
  name: string;
  color: string;
  a: number;
  visible: boolean;
}

interface StateSubscribeResponse {
  subscription_id: string;
  snapshot?: EditorUiStateSnapshot;
}

interface StateRequestFullResponse {
  snapshot: EditorUiStateSnapshot;
}

interface CommandResponse {
  success: boolean;
  message: string;
  mesh_changed: boolean;
  state: EditorUiStateSnapshot;
}

interface StateDeltaEvent {
  domain: string;
  sequence: number;
  full: boolean;
  snapshot: EditorUiStateSnapshot;
}

const ui = {
  container: requiredElement<HTMLElement>("renderer-container"),
  status: requiredElement<HTMLElement>("status"),
  connection: requiredElement<HTMLElement>("connection-indicator"),
  bridgeStatus: requiredElement<HTMLElement>("bridge-status"),
  dirty: requiredElement<HTMLElement>("dirty-indicator"),
  toolList: requiredElement<HTMLElement>("tool-list"),
  paletteList: requiredElement<HTMLElement>("palette-list"),
  stateDiagnostics: requiredElement<HTMLElement>("state-diagnostics"),
  viewportDiagnostics: requiredElement<HTMLElement>("viewport-diagnostics"),
  projectPath: requiredElement<HTMLInputElement>("project-path"),
  undoButton: requiredElement<HTMLButtonElement>("undo-button"),
  redoButton: requiredElement<HTMLButtonElement>("redo-button"),
  refreshButton: requiredElement<HTMLButtonElement>("refresh-button"),
  fitViewButton: requiredElement<HTMLButtonElement>("fit-view-button"),
  gridToggleButton: requiredElement<HTMLButtonElement>("grid-toggle-button"),
  wireframeToggleButton: requiredElement<HTMLButtonElement>("wireframe-toggle-button"),
  openButton: requiredElement<HTMLButtonElement>("open-button"),
  saveButton: requiredElement<HTMLButtonElement>("save-button"),
  menuBar: requiredElement<HTMLElement>("accessible-menu-bar"),
  hudModel: requiredElement<HTMLElement>("hud-model"),
  hudMesh: requiredElement<HTMLElement>("hud-mesh"),
  hudTool: requiredElement<HTMLElement>("hud-tool"),
};

let scene: VoxelForgeScene;
let currentState: EditorUiStateSnapshot | null = null;
let currentMesh: MeshSnapshotData | null = null;
let latestMetrics: RendererMetrics | null = null;
let gridVisible = true;
let wireframeVisible = false;
let editingLatencyMs = 0;

// Editing latency tracking stats (rolling window)
let editingLatencyTotalMs = 0;
let editingLatencyCount = 0;

// Last hovered voxel position for diagnostics
let lastHoveredVoxel: { x: number; y: number; z: number } | null = null;
let lastHoverNormal: { x: number; y: number; z: number } | null = null;

/** Loading placeholder element created during startup. */
let loadingPlaceholder: HTMLDivElement | null = null;

/** Command palette for CLI command execution. */
let commandPalette: CommandPalette | null = null;

/** Accessible renderer-owned menu surface. */
let accessibleMenu: AccessibleMenuSurface | null = null;

async function main(): Promise<void> {
  // Create a loading placeholder as a separate element so we can remove it later
  // without affecting the canvas that VoxelForgeScene appends to the container.
  const loadingEl = document.createElement("div");
  loadingEl.style.cssText = "color: #aaa; padding: 20px;";
  loadingEl.textContent = "Loading VoxelForge renderer…";
  loadingPlaceholder = loadingEl;
  ui.container.appendChild(loadingEl);

  scene = new VoxelForgeScene(ui.container);

  scene.onRenderComplete((metrics: RendererMetrics) => {
    latestMetrics = metrics;
    renderViewportDiagnostics();
    window.voxelforgeBridge?.sendMetrics({
      scene_construction_ms: metrics.scene_construction_ms,
      first_render_ms: metrics.first_render_ms,
      vertex_count: metrics.vertex_count,
      triangle_count: metrics.triangle_count,
      total_renderer_ms: metrics.total_renderer_ms,
    });
    // Remove the loading placeholder on first successful render
    clearLoadingPlaceholder();
  });

  wireControls();
  wireViewportEditing();

  try {
    await handshake();
    await subscribeState();
    await refreshMesh();
    setupBridgeEvents();
    setupMenuEventListeners();
    setupAccessibleMenu();
    setupCommandPalette();
    setBridgeStatus("Bridge connected. C# owns editor state; TS owns presentation.", true);
    clearLoadingPlaceholder();
  } catch (err) {
    console.error("[renderer] startup failed:", err);
    setBridgeStatus(`Startup failed: ${formatError(err)}`, false);
    clearLoadingPlaceholder();
    // Show error in container without removing the canvas
    const errorEl = document.createElement("div");
    errorEl.style.cssText = "color: #ff7979; padding: 20px;";
    errorEl.textContent = formatError(err);
    ui.container.appendChild(errorEl);
  }

  window.voxelforgeBridge?.notifyReady();
}

/** Remove the loading placeholder element if it still exists. */
function clearLoadingPlaceholder(): void {
  if (loadingPlaceholder && loadingPlaceholder.parentNode) {
    loadingPlaceholder.parentNode.removeChild(loadingPlaceholder);
    loadingPlaceholder = null;
  }
}

async function handshake(): Promise<void> {
  const handshakeResult = await window.voxelforgeBridge.request("bridge:handshake", {
    client_schema_version: "voxelforge@1",
    supported_capabilities: ["mesh_json", "state_snapshot", "state_delta", "commands", "history", "project_io"],
  }) as { sidecar_schema_version?: string; compatible?: boolean; supported_capabilities?: string[] };

  if (!handshakeResult.compatible) {
    throw new Error(`Incompatible VoxelForge schema version: ${handshakeResult.sidecar_schema_version ?? "unknown"}`);
  }

  ui.bridgeStatus.textContent = `Schema ${handshakeResult.sidecar_schema_version}; capabilities: ${(handshakeResult.supported_capabilities ?? []).join(", ")}`;
}

async function subscribeState(): Promise<void> {
  const response = await window.voxelforgeBridge.request("bridge:state-subscribe", {
    domains: ["document", "session", "history", "palette", "diagnostics"],
    delivery_mode: "snapshot",
    full_snapshot_on_subscribe: true,
  }) as StateSubscribeResponse;

  if (response.snapshot) {
    applyState(response.snapshot);
  }
}

function setupBridgeEvents(): void {
  window.voxelforgeBridge.onEvent("voxelforge:state-delta", (payload: unknown) => {
    const update = payload as StateDeltaEvent;
    if (update.full && update.snapshot) {
      applyState(update.snapshot);
    }
  });

  setupMeshSubscription();
}

function setupMeshSubscription(): void {
  let currentMeshId = currentMesh?.mesh_id ?? "";

  window.voxelforgeBridge.onEvent("voxelforge:mesh-update", (payload: unknown) => {
    const update = payload as MeshUpdateEventData;
    console.log("[renderer] Mesh update received:", update);

    if (update.base_mesh_id !== currentMeshId && update.update_type !== "full_replace") {
      void refreshMesh();
      return;
    }

    try {
      const metrics = scene.applyIncrementalUpdate(update);
      currentMeshId = update.base_mesh_id;
      latestMetrics = metrics;
      renderViewportDiagnostics();
      setStatus(`Mesh update: ${update.update_type}, regions ${update.changed_regions.length}`);
    } catch (err) {
      console.error("[renderer] Failed to apply mesh update:", err);
      void refreshMesh();
    }
  });

  window.voxelforgeBridge.onEvent("voxelforge:palette-update", (payload: unknown) => {
    const update = payload as PaletteUpdateEventData;
    console.log("[renderer] Palette update received:", update);
  });

  window.voxelforgeBridge.request("bridge:mesh-subscribe", {
    model_id: "",
    chunk_size: 16,
    send_full_snapshot_on_subscribe: false,
  }).catch((err: unknown) => {
    console.warn("[renderer] Mesh subscription failed (manual refresh still works):", err);
  });
}

/**
 * Wire up listeners for native Electron menu events sent from the main process.
 * Each menu item click maps to an existing runAction, executeCommand, or scene method.
 */
function setupMenuEventListeners(): void {
  // All standard menu commands now route through the shared dispatch table
  // so accessible menu and native IPC paths share the same handler code.
  const ch = MenuChannels;

  // ── File menu ──
  window.voxelforgeBridge.onEvent("menu:file-new", () => menuCommandHandlers[ch.FILE_NEW]?.());
  window.voxelforgeBridge.onEvent("menu:file-open", () => menuCommandHandlers[ch.FILE_OPEN]?.());
  window.voxelforgeBridge.onEvent("menu:file-save", () => menuCommandHandlers[ch.FILE_SAVE]?.());
  window.voxelforgeBridge.onEvent("menu:file-save-as", () => menuCommandHandlers[ch.FILE_SAVE_AS]?.());
  window.voxelforgeBridge.onEvent("menu:file-exit", () => menuCommandHandlers[ch.FILE_EXIT]?.());

  // ── Edit menu ──
  window.voxelforgeBridge.onEvent("menu:edit-undo", () => menuCommandHandlers[ch.EDIT_UNDO]?.());
  window.voxelforgeBridge.onEvent("menu:edit-redo", () => menuCommandHandlers[ch.EDIT_REDO]?.());
  window.voxelforgeBridge.onEvent("menu:edit-fill-region", () => menuCommandHandlers[ch.EDIT_FILL_REGION]?.());
  window.voxelforgeBridge.onEvent("menu:edit-palette-list", () => menuCommandHandlers[ch.EDIT_PALETTE_LIST]?.());
  window.voxelforgeBridge.onEvent("menu:edit-palette-add", () => menuCommandHandlers[ch.EDIT_PALETTE_ADD]?.());
  window.voxelforgeBridge.onEvent("menu:edit-regions-list", () => menuCommandHandlers[ch.EDIT_REGIONS_LIST]?.());
  window.voxelforgeBridge.onEvent("menu:edit-regions-label", () => menuCommandHandlers[ch.EDIT_REGIONS_LABEL]?.());
  window.voxelforgeBridge.onEvent("menu:edit-clear-all", () => menuCommandHandlers[ch.EDIT_CLEAR_ALL]?.());

  // ── View menu ──
  window.voxelforgeBridge.onEvent("menu:view-front", () => menuCommandHandlers[ch.VIEW_FRONT]?.());
  window.voxelforgeBridge.onEvent("menu:view-side", () => menuCommandHandlers[ch.VIEW_SIDE]?.());
  window.voxelforgeBridge.onEvent("menu:view-top", () => menuCommandHandlers[ch.VIEW_TOP]?.());
  window.voxelforgeBridge.onEvent("menu:view-wireframe", () => menuCommandHandlers[ch.VIEW_WIREFRAME]?.());
  window.voxelforgeBridge.onEvent("menu:view-grid-size", () => menuCommandHandlers[ch.VIEW_GRID_SIZE]?.());
  window.voxelforgeBridge.onEvent("menu:view-measure-grid", () => menuCommandHandlers[ch.VIEW_MEASURE_GRID]?.());
  window.voxelforgeBridge.onEvent("menu:view-measure-scale", () => menuCommandHandlers[ch.VIEW_MEASURE_SCALE]?.());
  window.voxelforgeBridge.onEvent("menu:view-bg-color", () => menuCommandHandlers[ch.VIEW_BG_COLOR]?.());

  // ── Raycast debug menu ──
  window.voxelforgeBridge.onEvent("menu:view-raycast-debug", () => menuCommandHandlers[ch.VIEW_RAYCAST_DEBUG]?.());

  // ── Help menu ──
  window.voxelforgeBridge.onEvent("menu:help-about", () => menuCommandHandlers[ch.HELP_ABOUT]?.());

  // ── Reference Model menu ──
  window.voxelforgeBridge.onEvent("menu:reference-model-load", () => menuCommandHandlers[ch.REFERENCE_MODEL_LOAD]?.());
  window.voxelforgeBridge.onEvent("menu:reference-model-list", () => menuCommandHandlers[ch.REFERENCE_MODEL_LIST]?.());
  window.voxelforgeBridge.onEvent("menu:reference-model-remove", () => menuCommandHandlers[ch.REFERENCE_MODEL_REMOVE]?.());
  window.voxelforgeBridge.onEvent("menu:reference-clear", () => menuCommandHandlers[ch.REFERENCE_CLEAR]?.());
  window.voxelforgeBridge.onEvent("menu:reference-transform", () => menuCommandHandlers[ch.REFERENCE_TRANSFORM]?.());
  window.voxelforgeBridge.onEvent("menu:reference-mode", () => menuCommandHandlers[ch.REFERENCE_MODE]?.());
  window.voxelforgeBridge.onEvent("menu:reference-visibility", () => menuCommandHandlers[ch.REFERENCE_VISIBILITY]?.());
  window.voxelforgeBridge.onEvent("menu:reference-scale", () => menuCommandHandlers[ch.REFERENCE_SCALE]?.());
  window.voxelforgeBridge.onEvent("menu:reference-rotate", () => menuCommandHandlers[ch.REFERENCE_ROTATE]?.());
  window.voxelforgeBridge.onEvent("menu:reference-orient", () => menuCommandHandlers[ch.REFERENCE_ORIENT]?.());
  window.voxelforgeBridge.onEvent("menu:reference-info", () => menuCommandHandlers[ch.REFERENCE_INFO]?.());
  // reference-animation: IPC version carries payload with action; dispatch the map handler
  // (defaults to "list") and the inner IPC-only handler for native submenu items.
  window.voxelforgeBridge.onEvent("menu:reference-animation", (payload: unknown) => {
    console.log("[renderer] Menu event received: menu:reference-animation");
    const data = payload as { action?: string };
    const action = data?.action ?? "list";
    const idx = promptInt("Animation — Reference Model Index:");
    if (idx === null) return;
    const clipPrompt = action === "play" ? window.prompt("Animation Play — Clip index or name (optional):", "0") : null;
    const args = [String(idx), action];
    if (clipPrompt) args.push(clipPrompt);
    void myraExecuteCommand(`Animation ${action}`, "refanim", args);
  });
  window.voxelforgeBridge.onEvent("menu:reference-texture-assign", () => menuCommandHandlers[ch.REFERENCE_TEXTURE_ASSIGN]?.());
  window.voxelforgeBridge.onEvent("menu:reference-emissive-assign", () => menuCommandHandlers[ch.REFERENCE_EMISSIVE_ASSIGN]?.());
  window.voxelforgeBridge.onEvent("menu:reference-meta-save", () => menuCommandHandlers[ch.REFERENCE_META_SAVE]?.());
  window.voxelforgeBridge.onEvent("menu:reference-meta-load", () => menuCommandHandlers[ch.REFERENCE_META_LOAD]?.());

  // ── Image Reference menu ──
  window.voxelforgeBridge.onEvent("menu:image-ref-load", () => menuCommandHandlers[ch.IMAGE_REF_LOAD]?.());
  window.voxelforgeBridge.onEvent("menu:image-ref-list", () => menuCommandHandlers[ch.IMAGE_REF_LIST]?.());
  window.voxelforgeBridge.onEvent("menu:image-ref-remove", () => menuCommandHandlers[ch.IMAGE_REF_REMOVE]?.());

  // ── Voxelize menu ──
  window.voxelforgeBridge.onEvent("menu:voxelize-execute", () => menuCommandHandlers[ch.VOXELIZE_EXECUTE]?.());
  window.voxelforgeBridge.onEvent("menu:voxelize-compare", () => menuCommandHandlers[ch.VOXELIZE_COMPARE]?.());
}

// ── Shared menu command dispatch table ──
//
// Single source of truth for menu command handling. Both the native IPC event
// listeners (setupMenuEventListeners) and the accessible menu surface dispatch
// (dispatchMenuCommand) look up handlers from this map, ensuring that all items
// in APP_MENU_MODEL are functionally covered and the two paths cannot drift.
//
// Channels are MenuChannels constants. Every enabled item in APP_MENU_MODEL
// must have an entry here; the test accessibility-contract.test.ts asserts this.

/**
 * Populate the shared dispatch registry with all menu command handlers.
 * Both native IPC event listeners (setupMenuEventListeners) and the accessible
 * menu surface dispatch (dispatchMenuCommand) look up handlers from this map,
 * ensuring that all items in APP_MENU_MODEL are functionally covered and the
 * two paths cannot drift.
 *
 * Channels are MenuChannels constants. Every enabled item in APP_MENU_MODEL
 * must have an entry here; the test accessibility-contract.test.ts asserts this.
 */
Object.assign(menuCommandHandlers, {
  // ── File menu ──
  [MenuChannels.FILE_NEW]: () => {
    console.log("[renderer] Menu command: file-new");
    if (window.confirm("Clear the current model? Unsaved changes will be lost.")) {
      void runAction("New Project", "bridge:project-new", {});
    }
  },
  [MenuChannels.FILE_OPEN]: () => {
    console.log("[renderer] Menu command: file-open");
    void handleFileOpen(createRendererDeps(
      myraExecuteCommand,
      runAction,
      setStatus,
      () => ui.projectPath.value,
    ));
  },
  [MenuChannels.FILE_SAVE]: () => {
    console.log("[renderer] Menu command: file-save");
    void runAction("Save", "bridge:project-save", { path: ui.projectPath.value });
  },
  [MenuChannels.FILE_SAVE_AS]: () => {
    console.log("[renderer] Menu command: file-save-as");
    void handleFileSaveAs(createRendererDeps(
      myraExecuteCommand,
      runAction,
      setStatus,
      () => ui.projectPath.value,
    ));
  },
  [MenuChannels.FILE_EXIT]: () => {
    console.log("[renderer] Menu command: file-exit");
  },

  // ── Edit menu ──
  [MenuChannels.EDIT_UNDO]: () => {
    console.log("[renderer] Menu command: edit-undo");
    void runAction("Undo", "bridge:history-undo", {});
  },
  [MenuChannels.EDIT_REDO]: () => {
    console.log("[renderer] Menu command: edit-redo");
    void runAction("Redo", "bridge:history-redo", {});
  },
  [MenuChannels.EDIT_FILL_REGION]: () => {
    console.log("[renderer] Menu command: edit-fill-region");
    const x1 = parseInt(window.prompt("Fill Region — X1:") ?? "", 10);
    const y1 = parseInt(window.prompt("Fill Region — Y1:") ?? "", 10);
    const z1 = parseInt(window.prompt("Fill Region — Z1:") ?? "", 10);
    const x2 = parseInt(window.prompt("Fill Region — X2:") ?? "", 10);
    const y2 = parseInt(window.prompt("Fill Region — Y2:") ?? "", 10);
    const z2 = parseInt(window.prompt("Fill Region — Z2:") ?? "", 10);
    const idx = parseInt(window.prompt("Fill Region — Palette Index:") ?? "1", 10);
    if (!isNaN(x1) && !isNaN(y1) && !isNaN(z1) && !isNaN(x2) && !isNaN(y2) && !isNaN(z2) && !isNaN(idx)) {
      void executeCommand("fill_box", { x1, y1, z1, x2, y2, z2, palette_index: idx });
    }
  },
  [MenuChannels.EDIT_PALETTE_LIST]: () => {
    console.log("[renderer] Menu command: edit-palette-list");
    void executeCommand("list_palette", {});
  },
  [MenuChannels.EDIT_PALETTE_ADD]: () => {
    console.log("[renderer] Menu command: edit-palette-add");
    const indexStr = window.prompt("Add Material — Index:") ?? "";
    const name = window.prompt("Add Material — Name:") ?? "";
    const r = parseInt(window.prompt("Add Material — R (0-255):") ?? "255", 10);
    const g = parseInt(window.prompt("Add Material — G (0-255):") ?? "255", 10);
    const b = parseInt(window.prompt("Add Material — B (0-255):") ?? "255", 10);
    const a = parseInt(window.prompt("Add Material — A (0-255, default 255):") ?? "255", 10);
    void executeCommand("set_palette_entry", {
      index: parseInt(indexStr, 10) || 0,
      name: name || "Material",
      r, g, b, a,
    });
  },
  [MenuChannels.EDIT_REGIONS_LIST]: () => {
    console.log("[renderer] Menu command: edit-regions-list");
    void executeCommand("list_regions", {});
  },
  [MenuChannels.EDIT_REGIONS_LABEL]: () => {
    console.log("[renderer] Menu command: edit-regions-label");
    const regionName = window.prompt("Label Voxel — Region Name:");
    const x = parseInt(window.prompt("Label Voxel — X:") ?? "", 10);
    const y = parseInt(window.prompt("Label Voxel — Y:") ?? "", 10);
    const z = parseInt(window.prompt("Label Voxel — Z:") ?? "", 10);
    if (regionName && !isNaN(x) && !isNaN(y) && !isNaN(z)) {
      void executeCommand("assign_voxels_to_region", {
        region_name: regionName, x, y, z,
      });
    }
  },
  [MenuChannels.EDIT_CLEAR_ALL]: () => {
    console.log("[renderer] Menu command: edit-clear-all");
    if (window.confirm("Remove all voxels from the model?")) {
      void executeCommand("clear_model", {});
    }
  },

  // ── View menu ──
  [MenuChannels.VIEW_FRONT]: () => {
    try { scene.snapCameraToView("front"); } catch (e) { console.warn("[menu] view-front:", e); }
  },
  [MenuChannels.VIEW_SIDE]: () => {
    try { scene.snapCameraToView("side"); } catch (e) { console.warn("[menu] view-side:", e); }
  },
  [MenuChannels.VIEW_TOP]: () => {
    try { scene.snapCameraToView("top"); } catch (e) { console.warn("[menu] view-top:", e); }
  },
  [MenuChannels.VIEW_WIREFRAME]: () => {
    try {
      wireframeVisible = !wireframeVisible;
      scene.setWireframeVisible(wireframeVisible);
      ui.wireframeToggleButton.classList.toggle("active", wireframeVisible);
    } catch (e) { console.warn("[menu] view-wireframe:", e); }
  },
  [MenuChannels.VIEW_GRID_SIZE]: () => {
    const sizeStr = window.prompt("Grid Size (1-256):", "32");
    const size = parseInt(sizeStr ?? "", 10);
    if (!isNaN(size) && size >= 1 && size <= 256) {
      void executeCommand("set_grid_hint", { size });
    }
  },
  [MenuChannels.VIEW_MEASURE_GRID]: () => {
    void executeCommand("toggle_measure_grid", {});
  },
  [MenuChannels.VIEW_MEASURE_SCALE]: () => {
    const scaleStr = window.prompt("Voxels per meter (e.g. 8):", "8");
    const scale = parseFloat(scaleStr ?? "");
    if (!isNaN(scale) && scale > 0) {
      void executeCommand("set_measure_scale", { voxels_per_meter: scale });
    }
  },
  [MenuChannels.VIEW_BG_COLOR]: () => {
    const r = parseInt(window.prompt("Background Color — R (0-255):", "43") ?? "43", 10);
    const g = parseInt(window.prompt("Background Color — G (0-255):", "43") ?? "43", 10);
    const b = parseInt(window.prompt("Background Color — B (0-255):", "43") ?? "43", 10);
    if (!isNaN(r) && !isNaN(g) && !isNaN(b)) {
      scene.setBackgroundColor(Math.max(0, Math.min(255, r)), Math.max(0, Math.min(255, g)), Math.max(0, Math.min(255, b)));
    }
  },
  [MenuChannels.VIEW_RAYCAST_DEBUG]: () => {
    const enabled = !scene.isRaycastDebugEnabled;
    scene.setRaycastDebugEnabled(enabled);
    setStatus(enabled ? "Raycast debug overlay enabled" : "Raycast debug overlay disabled");
  },

  // ── Reference Model menu ──
  [MenuChannels.REFERENCE_MODEL_LOAD]: () => {
    void handleReferenceModelLoad(createRendererDeps(
      myraExecuteCommand,
      runAction,
      setStatus,
      () => ui.projectPath.value,
    ));
  },
  [MenuChannels.REFERENCE_MODEL_LIST]: () => {
    void myraExecuteCommand("List Ref Models", "reflist", []);
  },
  [MenuChannels.REFERENCE_MODEL_REMOVE]: () => {
    const idx = promptInt("Remove Reference Model — enter index:");
    if (idx !== null) {
      void myraExecuteCommand("Remove Ref Model", "refremove", [String(idx)]);
    }
  },
  [MenuChannels.REFERENCE_CLEAR]: () => {
    if (window.confirm("Remove all reference models?")) {
      void myraExecuteCommand("Clear References", "refclear", []);
    }
  },
  [MenuChannels.REFERENCE_TRANSFORM]: () => {
    const idx = promptInt("Transform — Reference Model Index:");
    if (idx === null) return;
    const x = promptFloat("Transform — Position X:", "0"); if (x === null) return;
    const y = promptFloat("Transform — Position Y:", "0"); if (y === null) return;
    const z = promptFloat("Transform — Position Z:", "0"); if (z === null) return;
    const rx = promptFloat("Transform — Rotation X (degrees):", "0"); if (rx === null) return;
    const ry = promptFloat("Transform — Rotation Y (degrees):", "0"); if (ry === null) return;
    const rz = promptFloat("Transform — Rotation Z (degrees):", "0"); if (rz === null) return;
    const scale = promptFloat("Transform — Scale:", "1.0"); if (scale === null) return;
    void myraExecuteCommand("Transform", "reftransform", [
      String(idx), String(x), String(y), String(z),
      String(rx), String(ry), String(rz), String(scale),
    ]);
  },
  [MenuChannels.REFERENCE_MODE]: () => {
    const idx = promptInt("Render Mode — Reference Model Index:");
    if (idx === null) return;
    const mode = window.prompt("Render Mode — Enter mode (wireframe | solid | transparent):", "solid");
    if (mode) {
      void myraExecuteCommand("Set Mode", "refmode", [String(idx), mode]);
    }
  },
  [MenuChannels.REFERENCE_VISIBILITY]: () => {
    const idx = promptInt("Toggle Visibility — Reference Model Index:");
    if (idx === null) return;
    const show = window.confirm("Show reference model? (Cancel = hide)");
    void myraExecuteCommand("Visibility", show ? "refshow" : "refhide", [String(idx)]);
  },
  [MenuChannels.REFERENCE_SCALE]: () => {
    const idx = promptInt("Scale — Reference Model Index:");
    if (idx === null) return;
    const scale = promptFloat("Scale — Value:", "1.0");
    if (scale === null) return;
    void myraExecuteCommand("Scale", "refscale", [String(idx), String(scale)]);
  },
  [MenuChannels.REFERENCE_ROTATE]: () => {
    const idx = promptInt("Rotate — Reference Model Index:");
    if (idx === null) return;
    const axis = window.prompt("Rotate — Axis (x | y | z):", "y");
    if (!axis) return;
    const degrees = promptFloat("Rotate — Degrees (default 90):", "90");
    if (degrees === null) return;
    void myraExecuteCommand("Rotate", "refrotate", [String(idx), axis, String(degrees)]);
  },
  [MenuChannels.REFERENCE_ORIENT]: () => {
    const idx = promptInt("Auto-Orient — Reference Model Index:");
    if (idx === null) return;
    void myraExecuteCommand("Auto-Orient", "reforient", [String(idx)]);
  },
  [MenuChannels.REFERENCE_INFO]: () => {
    const idx = promptInt("Inspect — Reference Model Index:");
    if (idx === null) return;
    void myraExecuteCommand("Inspect", "refinfo", [String(idx)]);
  },
  [MenuChannels.REFERENCE_ANIMATION]: () => {
    // Accessible menu has one item "List Animation Clips" — default to list action
    const idx = promptInt("Animation — Reference Model Index:");
    if (idx === null) return;
    void myraExecuteCommand("Animation list", "refanim", [String(idx), "list"]);
  },
  [MenuChannels.REFERENCE_TEXTURE_ASSIGN]: () => {
    void handleReferenceTextureAssign(createRendererDeps(
      myraExecuteCommand,
      runAction,
      setStatus,
      () => ui.projectPath.value,
    ));
  },
  [MenuChannels.REFERENCE_EMISSIVE_ASSIGN]: () => {
    void handleReferenceEmissiveAssign(createRendererDeps(
      myraExecuteCommand,
      runAction,
      setStatus,
      () => ui.projectPath.value,
    ));
  },
  [MenuChannels.REFERENCE_META_SAVE]: () => {
    void handleReferenceMetaSave(createRendererDeps(
      myraExecuteCommand,
      runAction,
      setStatus,
      () => ui.projectPath.value,
    ));
  },
  [MenuChannels.REFERENCE_META_LOAD]: () => {
    void handleReferenceMetaLoad(createRendererDeps(
      myraExecuteCommand,
      runAction,
      setStatus,
      () => ui.projectPath.value,
    ));
  },

  // ── Image Reference menu ──
  [MenuChannels.IMAGE_REF_LOAD]: () => {
    void handleImageRefLoad(createRendererDeps(
      myraExecuteCommand,
      runAction,
      setStatus,
      () => ui.projectPath.value,
    ));
  },
  [MenuChannels.IMAGE_REF_LIST]: () => {
    void myraExecuteCommand("List Image Refs", "imglist", []);
  },
  [MenuChannels.IMAGE_REF_REMOVE]: () => {
    const idx = promptInt("Remove Image Ref — enter index:");
    if (idx !== null) {
      void myraExecuteCommand("Remove Image Ref", "imgremove", [String(idx)]);
    }
  },

  // ── Voxelize menu ──
  [MenuChannels.VOXELIZE_EXECUTE]: () => {
    const idx = promptInt("Voxelize — Reference Model Index:");
    if (idx === null) return;
    const resolution = promptInt("Voxelize — Resolution (2-256):", "32");
    if (resolution === null) return;
    const mode = window.prompt("Voxelize — Mode (surface | solid):", "solid");
    if (mode && !mode.match(/^(surface|solid)$/i)) {
      setStatus("Invalid mode. Use 'surface' or 'solid'.");
      return;
    }
    void myraExecuteCommand("Voxelize", "voxelize", [String(idx), String(resolution), mode ?? "solid"]);
  },
  [MenuChannels.VOXELIZE_COMPARE]: () => {
    const idx = promptInt("Voxelize Compare — Reference Model Index:");
    if (idx === null) return;
    const resolutions = window.prompt("Voxelize Compare — Resolutions (comma-separated, e.g. 16,32,64):", "16,32,64");
    if (!resolutions) return;
    const mode = window.prompt("Voxelize Compare — Mode (surface | solid):", "solid");
    if (mode && !mode.match(/^(surface|solid)$/i)) {
      setStatus("Invalid mode. Use 'surface' or 'solid'.");
      return;
    }
    void myraExecuteCommand("Voxelize Compare", "voxcompare", [String(idx), resolutions, mode ?? "solid"]);
  },

  // ── Help menu ──
  [MenuChannels.HELP_ABOUT]: () => {
    setStatus("About VoxelForge — Voxel Authoring Tool with LLM integration. Coordinate System: Y-up (right-handed). R=X, G=Y, B=Z.");
  },

  // ── Command palette / console ──
  [MenuChannels.COMMAND_PALETTE]: () => {
    commandPalette?.show();
  },
  [MenuChannels.COMMAND_PALETTE_AO_BAKE]: () => {
    commandPalette?.show("ao-bake");
  },
  [MenuChannels.COMMAND_PALETTE_EDGE_DARKEN]: () => {
    commandPalette?.show("edge-darken");
  },
  [MenuChannels.COMMAND_PALETTE_LIGHT_BAKE]: () => {
    commandPalette?.show("light-bake");
  },
  [MenuChannels.COMMAND_PALETTE_PALETTE_MAP]: () => {
    commandPalette?.show("palette-map");
  },
  [MenuChannels.COMMAND_PALETTE_PALETTE_REDUCE]: () => {
    commandPalette?.show("palette-reduce");
  },
  [MenuChannels.COMMAND_PALETTE_SCREENSHOT]: () => {
    commandPalette?.show("screenshot");
  },
});

/**
 * Initialize the renderer-owned accessible menu surface.
 * Creates a semantic menubar with ARIA roles, keyboard navigation, and focus styles
 * that routes commands through the same handler paths as native menu IPC events.
 */
function setupAccessibleMenu(): void {
  if (accessibleMenu) {
    accessibleMenu.destroy();
  }

  accessibleMenu = new AccessibleMenuSurface({
    container: ui.menuBar,
    onCommand: (channel: string, itemId: string) => {
      console.log(`[renderer] Accessible menu command activated: ${channel} (${itemId})`);
      dispatchMenuCommand(channel);
    },
  });
}

/**
 * Dispatch a menu command channel to the same handler path used by native menu IPC.
 * This ensures accessible menu items and native menu items share the same code path.
 */
function dispatchMenuCommand(channel: string): void {
  const handler = menuCommandHandlers[channel];
  if (handler) {
    handler();
  } else {
    console.log(`[renderer] Menu command not explicitly dispatched: ${channel}`);
  }
}

/**
 * Initialize the command palette and wire keyboard/menu event handlers.
 */
function setupCommandPalette(): void {
  commandPalette = new CommandPalette({
    execute: (label, command, args) => myraExecuteCommand(label, command, args),
    setStatus: (msg) => setStatus(msg),
  });

  // Keyboard shortcut: Ctrl+Shift+P or F6
  document.addEventListener("keydown", (event: KeyboardEvent) => {
    if ((event.ctrlKey || event.metaKey) && event.shiftKey && event.key === "P") {
      event.preventDefault();
      commandPalette?.show();
    }
    if (event.key === "F6" && !event.ctrlKey && !event.metaKey && !event.altKey) {
      event.preventDefault();
      commandPalette?.show();
    }
  });

  // Menu-triggered palette events
  window.voxelforgeBridge.onEvent("menu:command-palette", () => {
    commandPalette?.show();
  });

  window.voxelforgeBridge.onEvent("menu:cmd-ao-bake", () => {
    commandPalette?.show("ao-bake");
  });

  window.voxelforgeBridge.onEvent("menu:cmd-edge-darken", () => {
    commandPalette?.show("edge-darken");
  });

  window.voxelforgeBridge.onEvent("menu:cmd-light-bake", () => {
    commandPalette?.show("light-bake");
  });

  window.voxelforgeBridge.onEvent("menu:cmd-palette-map", () => {
    commandPalette?.show("palette-map");
  });

  window.voxelforgeBridge.onEvent("menu:cmd-palette-reduce", () => {
    commandPalette?.show("palette-reduce");
  });

  window.voxelforgeBridge.onEvent("menu:cmd-screenshot", () => {
    commandPalette?.show("screenshot");
  });
}

function wireControls(): void {
  ui.undoButton.addEventListener("click", () => void runAction("Undo", "bridge:history-undo", {}));
  ui.redoButton.addEventListener("click", () => void runAction("Redo", "bridge:history-redo", {}));
  ui.refreshButton.addEventListener("click", () => void refreshAll());
  ui.fitViewButton.addEventListener("click", () => scene.frameCurrentModel());
  ui.gridToggleButton.addEventListener("click", () => {
    gridVisible = !gridVisible;
    scene.setGridVisible(gridVisible);
    ui.gridToggleButton.classList.toggle("active", gridVisible);
  });
  ui.wireframeToggleButton.addEventListener("click", () => {
    wireframeVisible = !wireframeVisible;
    scene.setWireframeVisible(wireframeVisible);
    ui.wireframeToggleButton.classList.toggle("active", wireframeVisible);
  });
  ui.saveButton.addEventListener("click", () => void runAction("Save", "bridge:project-save", { path: ui.projectPath.value }));
  ui.openButton.addEventListener("click", () => void runAction("Open", "bridge:project-load", { path: ui.projectPath.value }));
}

function wireViewportEditing(): void {
  const canvas = scene.getCanvas();
  if (!canvas) {
    console.warn("[renderer] No WebGL canvas available for viewport editing.");
    return;
  }

  // ── Mouse click → raycast → C# editing command ──
  canvas.addEventListener("click", async (event: MouseEvent) => {
    const clientX = event.clientX;
    const clientY = event.clientY;
    const hit = scene.raycast(clientX, clientY);

    // Record raycast debug event when overlay is active
    if (scene.raycastDebugger.isEnabled) {
      const ndcData = computeScreenToNDC(clientX, clientY, canvas);
      const rayInfo = scene.getRayFromClient(clientX, clientY);
      if (hit) {
        scene.raycastDebugger.recordEvent(buildRaycastDebugEvent(
          clientX, clientY, hit, ndcData,
          hit.ray_direction ?? rayInfo?.rayDirection ?? { x: 0, y: 0, z: 0 },
          hit.hit_object_type ?? "voxel_mesh", hit.hit_object_id ?? canvas.id,
        ));
      } else {
        scene.raycastDebugger.recordEvent(buildRaycastMissDebugEvent(
          clientX, clientY, ndcData,
          rayInfo?.rayOrigin ?? { x: 0, y: 0, z: 0 },
          rayInfo?.rayDirection ?? { x: 0, y: 0, z: 0 },
          scene.getCamera().far,
        ));
      }
    }

    if (!hit) return;

    const state = currentState;
    if (!state) return;

    const tool = state.active_tool;
    const paletteIdx = state.active_palette_index;
    const shiftKey = event.shiftKey;

    const editStartMs = performance.now();

    try {
      // Shift+click always adds to selection regardless of active tool
      if (shiftKey) {
        await executeCommand("add_to_selection", {
          x: hit.position.x,
          y: hit.position.y,
          z: hit.position.z,
        });
        setStatus(`Added (${hit.position.x},${hit.position.y},${hit.position.z}) to selection`);
      } else {
        switch (tool) {
          case "place": {
            // Placement position = hit voxel + face normal
            const placePos = scene.computePlacementPosition(hit);
            await executeCommand("place_voxel", {
              x: placePos.x,
              y: placePos.y,
              z: placePos.z,
              palette_index: paletteIdx,
            });
            setStatus(`Placed voxel at (${placePos.x},${placePos.y},${placePos.z})`);
            break;
          }
          case "remove": {
            await executeCommand("remove_voxel", {
              x: hit.position.x,
              y: hit.position.y,
              z: hit.position.z,
            });
            setStatus(`Removed voxel at (${hit.position.x},${hit.position.y},${hit.position.z})`);
            break;
          }
          case "paint": {
            await executeCommand("paint_voxel", {
              x: hit.position.x,
              y: hit.position.y,
              z: hit.position.z,
              palette_index: paletteIdx,
            });
            setStatus(`Painted voxel at (${hit.position.x},${hit.position.y},${hit.position.z})`);
            break;
          }
          case "select": {
            await executeCommand("select_voxel", {
              x: hit.position.x,
              y: hit.position.y,
              z: hit.position.z,
            });
            setStatus(`Selected voxel at (${hit.position.x},${hit.position.y},${hit.position.z})`);
            break;
          }
          default:
            // Other tools (fill, label) require region/box selection;
            // not yet wired through click interaction.
            setStatus(`Tool "${tool}" requires additional interaction (not yet wired for click).`);
        }
      }
    } catch (err) {
      setStatus(`Edit failed: ${formatError(err)}`);
    }

    const editDurationMs = performance.now() - editStartMs;
    editingLatencyTotalMs += editDurationMs;
    editingLatencyCount++;
    editingLatencyMs = editingLatencyCount > 0 ? editingLatencyTotalMs / editingLatencyCount : 0;
    renderViewportDiagnostics();
  });

  // ── Mouse move → hover diagnostics ──
  canvas.addEventListener("mousemove", (event: MouseEvent) => {
    const hit = scene.raycast(event.clientX, event.clientY);
    if (hit) {
      lastHoveredVoxel = hit.position;
      lastHoverNormal = hit.normal;
    } else {
      lastHoveredVoxel = null;
      lastHoverNormal = null;
    }
  });

  // ── Keyboard shortcuts ──
  document.addEventListener("keydown", async (event: KeyboardEvent) => {
    if (event.ctrlKey || event.metaKey) {
      if (event.key === "z" && !event.shiftKey) {
        event.preventDefault();
        await runAction("Undo", "bridge:history-undo", {});
      } else if ((event.key === "z" && event.shiftKey) || event.key === "Z" || event.key === "y") {
        event.preventDefault();
        await runAction("Redo", "bridge:history-redo", {});
      }
    }
  });
}

async function runAction(label: string, channel: string, payload: unknown): Promise<void> {
  setBusy(true);
  setStatus(`${label}…`);
  try {
    const response = await window.voxelforgeBridge.request(channel, payload) as CommandResponse;
    applyState(response.state);
    if (response.mesh_changed) {
      await refreshMesh();
    }
    setStatus(response.message);
  } catch (err) {
    setStatus(`${label} failed: ${formatError(err)}`);
  } finally {
    setBusy(false);
  }
}

async function executeCommand(commandName: string, argumentsPayload: Record<string, unknown>): Promise<void> {
  await runAction("Command", "bridge:command-execute", {
    command_name: commandName,
    arguments: argumentsPayload,
  });
}

/**
 * Execute a Myra CLI command through the dedicated bridge channel.
 * Routes to voxelforge.myra.execute on the C# side, which dispatches
 * through the Myra Console CommandRouter.
 */
async function myraExecuteCommand(label: string, command: string, args: string[]): Promise<void> {
  console.log(`[renderer] bridge:myra-command-execute request: ${command} ${JSON.stringify(args)}`);
  setBusy(true);
  setStatus(`${label}…`);
  try {
    const response = await window.voxelforgeBridge.request("bridge:myra-command-execute", {
      command,
      args,
    }) as { success: boolean; message: string; state?: EditorUiStateSnapshot };
    console.log(`[renderer] bridge:myra-command-execute response: success=${response.success} message=${JSON.stringify(response.message)}`);
    if (response.state) {
      applyState(response.state);
    }
    setStatus(response.message);
  } catch (err) {
    console.log(`[renderer] bridge:myra-command-execute failed: ${formatError(err)}`);
    setStatus(`${label} failed: ${formatError(err)}`);
  } finally {
    setBusy(false);
  }
}

/** Prompt for a positive integer. */
function promptInt(label: string, defaultValue = "0"): number | null {
  const s = window.prompt(label, defaultValue);
  if (s === null) return null;
  const n = parseInt(s, 10);
  return isNaN(n) ? null : n;
}

/** Prompt for a float. */
function promptFloat(label: string, defaultValue = "0"): number | null {
  const s = window.prompt(label, defaultValue);
  if (s === null) return null;
  const n = parseFloat(s);
  return isNaN(n) ? null : n;
}

async function refreshAll(): Promise<void> {
  setBusy(true);
  try {
    const stateResponse = await window.voxelforgeBridge.request("bridge:state-request-full", {
      domains: ["document", "session", "history", "palette", "diagnostics"],
    }) as StateRequestFullResponse;
    applyState(stateResponse.snapshot);
    await refreshMesh();
    setStatus("Refreshed authoritative state and mesh from C#.");
  } catch (err) {
    setStatus(`Refresh failed: ${formatError(err)}`);
  } finally {
    setBusy(false);
  }
}

/**
 * Coalesced mesh refresh using the canonical render-scene snapshot channel.
 * Uses `bridge:render-snapshot` which provides the full RenderSceneSnapshot
 * contract (materials, textures, reference nodes, palette).
 * Falls back to buildMeshFromSnapshot which is kept for backward compat.
 */
const refreshMesh = createCoalescer(async (): Promise<void> => {
  try {
    // Try the canonical render-scene snapshot first
    const snapshotData = await window.voxelforgeBridge.request(
      "bridge:render-snapshot",
      {},
    ) as Record<string, unknown>;

    // Check if the response is a RenderSceneSnapshot (has schema_version)
    if (snapshotData && (snapshotData as { schema_version?: string }).schema_version) {
      const snapshot = snapshotData as unknown as RenderSceneSnapshot;
      const metrics = scene.buildFromSnapshot(snapshot);
      latestMetrics = metrics;
      if (wireframeVisible) scene.setWireframeVisible(true);
      renderViewportDiagnostics();
      return;
    }

    // Fallback: convert transitional mesh data
    const meshData = snapshotData as unknown as MeshSnapshotData;
    currentMesh = meshData;
    const metrics = scene.buildMeshFromSnapshot(meshData);
    latestMetrics = metrics;
    if (wireframeVisible) scene.setWireframeVisible(true);
    renderViewportDiagnostics();
  } catch (err) {
    console.error("[renderer] refreshMesh failed:", err);
    throw err;
  }
});

function applyState(state: EditorUiStateSnapshot): void {
  currentState = state;
  ui.projectPath.value = state.project_path || ui.projectPath.value;
  ui.undoButton.disabled = !state.can_undo;
  ui.redoButton.disabled = !state.can_redo;
  ui.dirty.textContent = state.is_dirty ? "dirty" : "clean";
  ui.dirty.className = state.is_dirty ? "warn" : "ok";
  ui.hudModel.textContent = `Model: ${state.model_id} (${state.voxel_count} voxels)`;
  ui.hudTool.textContent = `Tool: ${state.active_tool} · palette ${state.active_palette_index}`;
  renderTools(state);
  renderPalette(state);
  renderStateDiagnostics(state);
  setStatus(state.status_message);
}

function renderTools(state: EditorUiStateSnapshot): void {
  ui.toolList.replaceChildren();
  for (const tool of state.available_tools) {
    const button = document.createElement("button");
    button.textContent = titleCase(tool);
    button.classList.toggle("active", tool === state.active_tool);
    button.addEventListener("click", () => void executeCommand("set_active_tool", { tool }));
    ui.toolList.appendChild(button);
  }
}

function renderPalette(state: EditorUiStateSnapshot): void {
  ui.paletteList.replaceChildren();
  for (const entry of state.palette_entries.filter((p) => p.visible)) {
    const button = document.createElement("button");
    button.className = "palette-entry";
    button.classList.toggle("active", entry.index === state.active_palette_index);
    button.addEventListener("click", () => void executeCommand("set_active_palette", { palette_index: entry.index }));

    const swatch = document.createElement("span");
    swatch.className = "palette-swatch";
    swatch.style.background = entry.color;

    const name = document.createElement("span");
    name.textContent = entry.name;

    const index = document.createElement("span");
    index.className = "palette-index";
    index.textContent = `#${entry.index}`;

    button.append(swatch, name, index);
    ui.paletteList.appendChild(button);
  }
}

function renderStateDiagnostics(state: EditorUiStateSnapshot): void {
  const bounds = state.bounds
    ? `${state.bounds.min_x},${state.bounds.min_y},${state.bounds.min_z} → ${state.bounds.max_x},${state.bounds.max_y},${state.bounds.max_z}`
    : "empty";
  setKeyValues(ui.stateDiagnostics, [
    ["model", state.model_id],
    ["project", state.project_path || "unsaved"],
    ["dirty", state.is_dirty ? "yes" : "no"],
    ["voxels", String(state.voxel_count)],
    ["bounds", bounds],
    ["grid hint", String(state.grid_hint)],
    ["tool", state.active_tool],
    ["palette", String(state.active_palette_index)],
    ["history", `undo ${state.undo_depth}, redo ${state.redo_depth}`],
    ["selection", `${state.selected_voxel_count} voxel(s)`],
  ]);
}

function renderViewportDiagnostics(): void {
  if (!currentMesh) return;
  ui.hudMesh.textContent = `Mesh: ${currentMesh.vertex_count} verts · ${currentMesh.triangle_count} tris`;

  const hoverInfo = lastHoveredVoxel
    ? `hit (${lastHoveredVoxel.x},${lastHoveredVoxel.y},${lastHoveredVoxel.z})`
    : "—";
  const hoverNormalInfo = lastHoverNormal
    ? `n (${lastHoverNormal.x},${lastHoverNormal.y},${lastHoverNormal.z})`
    : "—";

  setKeyValues(ui.viewportDiagnostics, [
    ["mesh id", currentMesh.mesh_id],
    ["vertices", String(currentMesh.vertex_count)],
    ["triangles", String(currentMesh.triangle_count)],
    ["indices", String(currentMesh.index_count)],
    ["mesh build", `${currentMesh.metrics?.mesh_generation_ms ?? 0} ms`],
    ["serialize", `${currentMesh.metrics?.serialization_ms ?? 0} ms`],
    ["scene", `${latestMetrics?.scene_construction_ms.toFixed(2) ?? "—"} ms`],
    ["first render", `${latestMetrics?.first_render_ms.toFixed(2) ?? "—"} ms`],
    ["webgl", latestMetrics?.webgl_fallback ? "fallback" : "active"],
    ["hover", hoverInfo],
    ["hover normal", hoverNormalInfo],
    ["editing latency", editingLatencyCount > 0 ? `${editingLatencyMs.toFixed(1)} ms avg (${editingLatencyCount} edits)` : "—"],
  ]);
}

function setKeyValues(container: HTMLElement, rows: [string, string][]): void {
  container.replaceChildren();
  for (const [key, value] of rows) {
    const keyEl = document.createElement("span");
    keyEl.textContent = key;
    const valueEl = document.createElement("span");
    valueEl.textContent = value;
    container.append(keyEl, valueEl);
  }
}

function setBusy(isBusy: boolean): void {
  ui.openButton.disabled = isBusy;
  ui.saveButton.disabled = isBusy;
  ui.refreshButton.disabled = isBusy;
}

function setBridgeStatus(message: string, connected: boolean): void {
  ui.bridgeStatus.textContent = message;
  ui.connection.textContent = connected ? "Connected" : "Bridge issue";
  ui.connection.className = connected ? "ok" : "warn";
}

function setStatus(message: string): void {
  ui.status.textContent = message;
}

// titleCase, formatError, escapeHtml are now imported from shared/string-utils

function requiredElement<T extends HTMLElement>(id: string): T {
  const element = document.getElementById(id);
  if (!element) {
    throw new Error(`Missing required element #${id}`);
  }
  return element as T;
}

main().catch((err) => console.error("[renderer] Unhandled error:", err));
