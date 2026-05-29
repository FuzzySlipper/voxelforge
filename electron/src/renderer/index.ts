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
import { handleReferenceModelLoad } from "./menu-handlers";
import { AccessibleMenuSurface } from "./accessible-menu-surface";
import { MenuChannels } from "../shared/menu-channels";

declare global {
  interface Window {
    voxelforgeBridge: {
      request(channel: string, payload: unknown): Promise<unknown>;
      onEvent(channel: string, callback: (payload: unknown) => void): () => void;
      notifyReady(): void;
      sendMetrics(metrics: Record<string, number>): void;
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
  // ── File menu ──
  window.voxelforgeBridge.onEvent("menu:file-new", () => {
    console.log("[renderer] Menu event received: menu:file-new");
    // Confirm in the renderer before clearing
    if (window.confirm("Clear the current model? Unsaved changes will be lost.")) {
      void runAction("New Project", "bridge:project-new", {});
    } else {
      console.log("[renderer] menu:file-new cancelled by user");
    }
  });

  window.voxelforgeBridge.onEvent("menu:file-open", () => {
    console.log("[renderer] Menu event received: menu:file-open");
    const path = window.prompt("Enter project file path (.vforge):", ui.projectPath.value);
    if (path) {
      ui.projectPath.value = path;
      void runAction("Open", "bridge:project-load", { path });
    } else {
      console.log("[renderer] menu:file-open cancelled by user");
    }
  });

  window.voxelforgeBridge.onEvent("menu:file-save", () => {
    console.log("[renderer] Menu event received: menu:file-save");
    void runAction("Save", "bridge:project-save", { path: ui.projectPath.value });
  });

  window.voxelforgeBridge.onEvent("menu:file-save-as", () => {
    console.log("[renderer] Menu event received: menu:file-save-as");
    const path = window.prompt("Save project as (.vforge):", ui.projectPath.value);
    if (path) {
      const fullPath = path.endsWith(".vforge") ? path : path + ".vforge";
      ui.projectPath.value = fullPath;
      void runAction("Save As", "bridge:project-save", { path: fullPath });
    } else {
      console.log("[renderer] menu:file-save-as cancelled by user");
    }
  });

  window.voxelforgeBridge.onEvent("menu:file-exit", () => {
    console.log("[renderer] Menu event received: menu:file-exit");
    // Main process already closes the window; we just log
    console.log("[renderer] Exit requested via native menu.");
  });

  // ── Edit menu ──
  window.voxelforgeBridge.onEvent("menu:edit-undo", () => {
    console.log("[renderer] Menu event received: menu:edit-undo");
    void runAction("Undo", "bridge:history-undo", {});
  });

  window.voxelforgeBridge.onEvent("menu:edit-redo", () => {
    console.log("[renderer] Menu event received: menu:edit-redo");
    void runAction("Redo", "bridge:history-redo", {});
  });

  window.voxelforgeBridge.onEvent("menu:edit-fill-region", () => {
    console.log("[renderer] Menu event received: menu:edit-fill-region");
    const x1 = parseInt(window.prompt("Fill Region — X1:") ?? "", 10);
    const y1 = parseInt(window.prompt("Fill Region — Y1:") ?? "", 10);
    const z1 = parseInt(window.prompt("Fill Region — Z1:") ?? "", 10);
    const x2 = parseInt(window.prompt("Fill Region — X2:") ?? "", 10);
    const y2 = parseInt(window.prompt("Fill Region — Y2:") ?? "", 10);
    const z2 = parseInt(window.prompt("Fill Region — Z2:") ?? "", 10);
    const idx = parseInt(window.prompt("Fill Region — Palette Index:") ?? "1", 10);
    if (!isNaN(x1) && !isNaN(y1) && !isNaN(z1) && !isNaN(x2) && !isNaN(y2) && !isNaN(z2) && !isNaN(idx)) {
      void executeCommand("fill_box", {
        x1, y1, z1, x2, y2, z2, palette_index: idx,
      });
    } else {
      console.log("[renderer] menu:edit-fill-region cancelled or invalid input");
    }
  });

  window.voxelforgeBridge.onEvent("menu:edit-palette-list", () => {
    console.log("[renderer] Menu event received: menu:edit-palette-list");
    void executeCommand("list_palette", {});
  });

  window.voxelforgeBridge.onEvent("menu:edit-palette-add", () => {
    console.log("[renderer] Menu event received: menu:edit-palette-add");
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
  });

  window.voxelforgeBridge.onEvent("menu:edit-regions-list", () => {
    console.log("[renderer] Menu event received: menu:edit-regions-list");
    void executeCommand("list_regions", {});
  });

  window.voxelforgeBridge.onEvent("menu:edit-regions-label", () => {
    console.log("[renderer] Menu event received: menu:edit-regions-label");
    const regionName = window.prompt("Label Voxel — Region Name:");
    const x = parseInt(window.prompt("Label Voxel — X:") ?? "", 10);
    const y = parseInt(window.prompt("Label Voxel — Y:") ?? "", 10);
    const z = parseInt(window.prompt("Label Voxel — Z:") ?? "", 10);
    if (regionName && !isNaN(x) && !isNaN(y) && !isNaN(z)) {
      void executeCommand("assign_voxels_to_region", {
        region_name: regionName, x, y, z,
      });
    } else {
      console.log("[renderer] menu:edit-regions-label cancelled or invalid input");
    }
  });

  window.voxelforgeBridge.onEvent("menu:edit-clear-all", () => {
    console.log("[renderer] Menu event received: menu:edit-clear-all");
    if (window.confirm("Remove all voxels from the model?")) {
      void executeCommand("clear_model", {});
    } else {
      console.log("[renderer] menu:edit-clear-all cancelled by user");
    }
  });

  // ── View menu ──
  window.voxelforgeBridge.onEvent("menu:view-front", () => {
    console.log("[renderer] Menu event received: menu:view-front");
    try { scene.snapCameraToView("front"); } catch (e) { console.warn("[menu] view-front:", e); }
  });

  window.voxelforgeBridge.onEvent("menu:view-side", () => {
    console.log("[renderer] Menu event received: menu:view-side");
    try { scene.snapCameraToView("side"); } catch (e) { console.warn("[menu] view-side:", e); }
  });

  window.voxelforgeBridge.onEvent("menu:view-top", () => {
    console.log("[renderer] Menu event received: menu:view-top");
    try { scene.snapCameraToView("top"); } catch (e) { console.warn("[menu] view-top:", e); }
  });

  window.voxelforgeBridge.onEvent("menu:view-wireframe", () => {
    console.log("[renderer] Menu event received: menu:view-wireframe");
    try {
      wireframeVisible = !wireframeVisible;
      scene.setWireframeVisible(wireframeVisible);
      ui.wireframeToggleButton.classList.toggle("active", wireframeVisible);
    } catch (e) { console.warn("[menu] view-wireframe:", e); }
  });

  window.voxelforgeBridge.onEvent("menu:view-grid-size", () => {
    console.log("[renderer] Menu event received: menu:view-grid-size");
    const sizeStr = window.prompt("Grid Size (1-256):", "32");
    const size = parseInt(sizeStr ?? "", 10);
    if (!isNaN(size) && size >= 1 && size <= 256) {
      void executeCommand("set_grid_hint", { size });
    } else {
      console.log("[renderer] menu:view-grid-size cancelled or invalid input");
    }
  });

  window.voxelforgeBridge.onEvent("menu:view-measure-grid", () => {
    console.log("[renderer] Menu event received: menu:view-measure-grid");
    void executeCommand("toggle_measure_grid", {});
  });

  window.voxelforgeBridge.onEvent("menu:view-measure-scale", () => {
    console.log("[renderer] Menu event received: menu:view-measure-scale");
    const scaleStr = window.prompt("Voxels per meter (e.g. 8):", "8");
    const scale = parseFloat(scaleStr ?? "");
    if (!isNaN(scale) && scale > 0) {
      void executeCommand("set_measure_scale", { voxels_per_meter: scale });
    } else {
      console.log("[renderer] menu:view-measure-scale cancelled or invalid input");
    }
  });

  window.voxelforgeBridge.onEvent("menu:view-bg-color", () => {
    console.log("[renderer] Menu event received: menu:view-bg-color");
    const r = parseInt(window.prompt("Background Color — R (0-255):", "43") ?? "43", 10);
    const g = parseInt(window.prompt("Background Color — G (0-255):", "43") ?? "43", 10);
    const b = parseInt(window.prompt("Background Color — B (0-255):", "43") ?? "43", 10);
    if (!isNaN(r) && !isNaN(g) && !isNaN(b)) {
      scene.setBackgroundColor(Math.max(0, Math.min(255, r)), Math.max(0, Math.min(255, g)), Math.max(0, Math.min(255, b)));
    }
  });

  // ── Help menu ──
  window.voxelforgeBridge.onEvent("menu:help-about", () => {
    console.log("[renderer] Menu event received: menu:help-about");
    setStatus("About VoxelForge — Voxel Authoring Tool with LLM integration. Coordinate System: Y-up (right-handed). R=X, G=Y, B=Z.");
  });

  // ── Reference Model menu ──
  window.voxelforgeBridge.onEvent("menu:reference-model-load", () => {
    void handleReferenceModelLoad(createRendererDeps(
      myraExecuteCommand,
      runAction,
      setStatus,
      () => ui.projectPath.value,
    ));
  });

  window.voxelforgeBridge.onEvent("menu:reference-model-list", () => {
    console.log("[renderer] Menu event received: menu:reference-model-list");
    void myraExecuteCommand("List Ref Models", "reflist", []);
  });

  window.voxelforgeBridge.onEvent("menu:reference-model-remove", () => {
    console.log("[renderer] Menu event received: menu:reference-model-remove");
    const idx = promptInt("Remove Reference Model — enter index:");
    if (idx !== null) {
      void myraExecuteCommand("Remove Ref Model", "refremove", [String(idx)]);
    } else {
      console.log("[renderer] menu:reference-model-remove cancelled by user");
    }
  });

  window.voxelforgeBridge.onEvent("menu:reference-clear", () => {
    console.log("[renderer] Menu event received: menu:reference-clear");
    if (window.confirm("Remove all reference models?")) {
      void myraExecuteCommand("Clear References", "refclear", []);
    } else {
      console.log("[renderer] menu:reference-clear cancelled by user");
    }
  });

  window.voxelforgeBridge.onEvent("menu:reference-transform", () => {
    console.log("[renderer] Menu event received: menu:reference-transform");
    const idx = promptInt("Transform — Reference Model Index:");
    if (idx === null) {
      console.log("[renderer] menu:reference-transform cancelled by user");
      return;
    }
    const x = promptFloat("Transform — Position X:", "0");
    if (x === null) return;
    const y = promptFloat("Transform — Position Y:", "0");
    if (y === null) return;
    const z = promptFloat("Transform — Position Z:", "0");
    if (z === null) return;
    const rx = promptFloat("Transform — Rotation X (degrees):", "0");
    if (rx === null) return;
    const ry = promptFloat("Transform — Rotation Y (degrees):", "0");
    if (ry === null) return;
    const rz = promptFloat("Transform — Rotation Z (degrees):", "0");
    if (rz === null) return;
    const scale = promptFloat("Transform — Scale:", "1.0");
    if (scale === null) return;
    void myraExecuteCommand("Transform", "reftransform", [
      String(idx), String(x), String(y), String(z),
      String(rx), String(ry), String(rz), String(scale),
    ]);
  });

  window.voxelforgeBridge.onEvent("menu:reference-mode", () => {
    console.log("[renderer] Menu event received: menu:reference-mode");
    const idx = promptInt("Render Mode — Reference Model Index:");
    if (idx === null) {
      console.log("[renderer] menu:reference-mode cancelled by user");
      return;
    }
    const mode = window.prompt("Render Mode — Enter mode (wireframe | solid | transparent):", "solid");
    if (mode) {
      void myraExecuteCommand("Set Mode", "refmode", [String(idx), mode]);
    } else {
      console.log("[renderer] menu:reference-mode cancelled — no mode entered");
    }
  });

  window.voxelforgeBridge.onEvent("menu:reference-visibility", () => {
    console.log("[renderer] Menu event received: menu:reference-visibility");
    const idx = promptInt("Toggle Visibility — Reference Model Index:");
    if (idx === null) {
      console.log("[renderer] menu:reference-visibility cancelled by user");
      return;
    }
    const show = window.confirm("Show reference model? (Cancel = hide)");
    void myraExecuteCommand("Visibility", show ? "refshow" : "refhide", [String(idx)]);
  });

  window.voxelforgeBridge.onEvent("menu:reference-scale", () => {
    console.log("[renderer] Menu event received: menu:reference-scale");
    const idx = promptInt("Scale — Reference Model Index:");
    if (idx === null) {
      console.log("[renderer] menu:reference-scale cancelled by user");
      return;
    }
    const scale = promptFloat("Scale — Value:", "1.0");
    if (scale === null) {
      console.log("[renderer] menu:reference-scale cancelled — no scale entered");
      return;
    }
    void myraExecuteCommand("Scale", "refscale", [String(idx), String(scale)]);
  });

  window.voxelforgeBridge.onEvent("menu:reference-rotate", () => {
    console.log("[renderer] Menu event received: menu:reference-rotate");
    const idx = promptInt("Rotate — Reference Model Index:");
    if (idx === null) {
      console.log("[renderer] menu:reference-rotate cancelled by user");
      return;
    }
    const axis = window.prompt("Rotate — Axis (x | y | z):", "y");
    if (!axis) {
      console.log("[renderer] menu:reference-rotate cancelled — no axis entered");
      return;
    }
    const degrees = promptFloat("Rotate — Degrees (default 90):", "90");
    if (degrees === null) {
      console.log("[renderer] menu:reference-rotate cancelled — no degrees entered");
      return;
    }
    void myraExecuteCommand("Rotate", "refrotate", [String(idx), axis, String(degrees)]);
  });

  window.voxelforgeBridge.onEvent("menu:reference-orient", () => {
    console.log("[renderer] Menu event received: menu:reference-orient");
    const idx = promptInt("Auto-Orient — Reference Model Index:");
    if (idx === null) {
      console.log("[renderer] menu:reference-orient cancelled by user");
      return;
    }
    void myraExecuteCommand("Auto-Orient", "reforient", [String(idx)]);
  });

  window.voxelforgeBridge.onEvent("menu:reference-info", () => {
    console.log("[renderer] Menu event received: menu:reference-info");
    const idx = promptInt("Inspect — Reference Model Index:");
    if (idx === null) {
      console.log("[renderer] menu:reference-info cancelled by user");
      return;
    }
    void myraExecuteCommand("Inspect", "refinfo", [String(idx)]);
  });

  window.voxelforgeBridge.onEvent("menu:reference-animation", (payload: unknown) => {
    console.log("[renderer] Menu event received: menu:reference-animation");
    const data = payload as { action?: string };
    const action = data?.action ?? "list";
    const idx = promptInt("Animation — Reference Model Index:");
    if (idx === null) {
      console.log("[renderer] menu:reference-animation cancelled by user");
      return;
    }
    const clipPrompt = action === "play" ? window.prompt("Animation Play — Clip index or name (optional):", "0") : null;
    const args = [String(idx), action];
    if (clipPrompt) args.push(clipPrompt);
    const label = `Animation ${action}`;
    void myraExecuteCommand(label, "refanim", args);
  });

  window.voxelforgeBridge.onEvent("menu:reference-texture-assign", () => {
    console.log("[renderer] Menu event received: menu:reference-texture-assign");
    const idx = promptInt("Texture Assign — Reference Model Index:");
    if (idx === null) {
      console.log("[renderer] menu:reference-texture-assign cancelled by user");
      return;
    }
    const texPath = window.prompt("Texture Assign — Texture file path:", "");
    if (!texPath) {
      console.log("[renderer] menu:reference-texture-assign cancelled — no path entered");
      return;
    }
    const meshIdx = window.prompt("Texture Assign — Mesh index (optional, blank for all):", "");
    const args = [String(idx), texPath];
    if (meshIdx) args.push(meshIdx);
    void myraExecuteCommand("Assign Texture", "reftex", args);
  });

  window.voxelforgeBridge.onEvent("menu:reference-emissive-assign", () => {
    console.log("[renderer] Menu event received: menu:reference-emissive-assign");
    const idx = promptInt("Emissive Assign — Reference Model Index:");
    if (idx === null) {
      console.log("[renderer] menu:reference-emissive-assign cancelled by user");
      return;
    }
    const texPath = window.prompt("Emissive Assign — Texture file path:", "");
    if (!texPath) {
      console.log("[renderer] menu:reference-emissive-assign cancelled — no path entered");
      return;
    }
    const brightness = promptFloat("Emissive Assign — Brightness (default 1.0):", "1.0");
    if (brightness === null) {
      console.log("[renderer] menu:reference-emissive-assign cancelled — no brightness entered");
      return;
    }
    const meshIdx = window.prompt("Emissive Assign — Mesh index (optional):", "");
    const args = [String(idx), texPath, String(brightness)];
    if (meshIdx) args.push(meshIdx);
    void myraExecuteCommand("Assign Emissive", "reftex-emissive", args);
  });

  window.voxelforgeBridge.onEvent("menu:reference-meta-save", () => {
    console.log("[renderer] Menu event received: menu:reference-meta-save");
    const idx = promptInt("Save Meta — Reference Model Index:");
    if (idx === null) {
      console.log("[renderer] menu:reference-meta-save cancelled by user");
      return;
    }
    const path = promptPath("Save Meta — Enter .refmeta path:", `${idx}.refmeta`);
    if (path) {
      void myraExecuteCommand("Save Meta", "refsave", [String(idx), path]);
    } else {
      console.log("[renderer] menu:reference-meta-save cancelled — no path entered");
    }
  });

  window.voxelforgeBridge.onEvent("menu:reference-meta-load", () => {
    console.log("[renderer] Menu event received: menu:reference-meta-load");
    const path = promptPath("Load Meta — Enter .refmeta file path:", "");
    if (path) {
      void myraExecuteCommand("Load Meta", "refloadmeta", [path]);
    } else {
      console.log("[renderer] menu:reference-meta-load cancelled — no path entered");
    }
  });

  // ── Image Reference menu ──
  window.voxelforgeBridge.onEvent("menu:image-ref-load", () => {
    console.log("[renderer] Menu event received: menu:image-ref-load");
    const path = promptPath("Load Image Reference — Enter file path:", "");
    if (path) {
      void myraExecuteCommand("Load Image Ref", "imgload", [path]);
    } else {
      console.log("[renderer] menu:image-ref-load cancelled — no path entered");
    }
  });

  window.voxelforgeBridge.onEvent("menu:image-ref-list", () => {
    console.log("[renderer] Menu event received: menu:image-ref-list");
    void myraExecuteCommand("List Image Refs", "imglist", []);
  });

  window.voxelforgeBridge.onEvent("menu:image-ref-remove", () => {
    console.log("[renderer] Menu event received: menu:image-ref-remove");
    const idx = promptInt("Remove Image Ref — enter index:");
    if (idx !== null) {
      void myraExecuteCommand("Remove Image Ref", "imgremove", [String(idx)]);
    } else {
      console.log("[renderer] menu:image-ref-remove cancelled by user");
    }
  });

  // ── Voxelize menu ──
  window.voxelforgeBridge.onEvent("menu:voxelize-execute", () => {
    console.log("[renderer] Menu event received: menu:voxelize-execute");
    const idx = promptInt("Voxelize — Reference Model Index:");
    if (idx === null) {
      console.log("[renderer] menu:voxelize-execute cancelled by user");
      return;
    }
    const resolution = promptInt("Voxelize — Resolution (2-256):", "32");
    if (resolution === null) {
      console.log("[renderer] menu:voxelize-execute cancelled — no resolution entered");
      return;
    }
    const mode = window.prompt("Voxelize — Mode (surface | solid):", "solid");
    if (mode && !mode.match(/^(surface|solid)$/i)) {
      setStatus("Invalid mode. Use 'surface' or 'solid'.");
      console.log("[renderer] menu:voxelize-execute invalid mode:", mode);
      return;
    }
    void myraExecuteCommand("Voxelize", "voxelize", [String(idx), String(resolution), mode ?? "solid"]);
  });

  window.voxelforgeBridge.onEvent("menu:voxelize-compare", () => {
    console.log("[renderer] Menu event received: menu:voxelize-compare");
    const idx = promptInt("Voxelize Compare — Reference Model Index:");
    if (idx === null) {
      console.log("[renderer] menu:voxelize-compare cancelled by user");
      return;
    }
    const resolutions = window.prompt("Voxelize Compare — Resolutions (comma-separated, e.g. 16,32,64):", "16,32,64");
    if (!resolutions) {
      console.log("[renderer] menu:voxelize-compare cancelled — no resolutions entered");
      return;
    }
    const mode = window.prompt("Voxelize Compare — Mode (surface | solid):", "solid");
    if (mode && !mode.match(/^(surface|solid)$/i)) {
      setStatus("Invalid mode. Use 'surface' or 'solid'.");
      console.log("[renderer] menu:voxelize-compare invalid mode:", mode);
      return;
    }
    void myraExecuteCommand("Voxelize Compare", "voxcompare", [String(idx), resolutions, mode ?? "solid"]);
  });
}

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
  // Build deps for handlers that need them
  const deps = createRendererDeps(
    myraExecuteCommand,
    runAction,
    setStatus,
    () => ui.projectPath.value,
  );

  switch (channel) {
    // ── File menu ──
    case MenuChannels.FILE_NEW:
      console.log("[renderer] Menu command: file-new");
      if (window.confirm("Clear the current model? Unsaved changes will be lost.")) {
        void runAction("New Project", "bridge:project-new", {});
      }
      break;
    case MenuChannels.FILE_OPEN:
      console.log("[renderer] Menu command: file-open");
      {
        const path = promptPath("Enter project file path (.vforge):", ui.projectPath.value);
        if (path) {
          ui.projectPath.value = path;
          void runAction("Open", "bridge:project-load", { path });
        }
      }
      break;
    case MenuChannels.FILE_SAVE:
      console.log("[renderer] Menu command: file-save");
      void runAction("Save", "bridge:project-save", { path: ui.projectPath.value });
      break;
    case MenuChannels.FILE_SAVE_AS:
      console.log("[renderer] Menu command: file-save-as");
      {
        const path = promptPath("Save project as (.vforge):", ui.projectPath.value);
        if (path) {
          const fullPath = path.endsWith(".vforge") ? path : path + ".vforge";
          ui.projectPath.value = fullPath;
          void runAction("Save As", "bridge:project-save", { path: fullPath });
        }
      }
      break;
    case MenuChannels.FILE_EXIT:
      console.log("[renderer] Menu command: file-exit");
      break;

    // ── Edit menu ──
    case MenuChannels.EDIT_UNDO:
      console.log("[renderer] Menu command: edit-undo");
      void runAction("Undo", "bridge:history-undo", {});
      break;
    case MenuChannels.EDIT_REDO:
      console.log("[renderer] Menu command: edit-redo");
      void runAction("Redo", "bridge:history-redo", {});
      break;
    case MenuChannels.EDIT_FILL_REGION:
      console.log("[renderer] Menu command: edit-fill-region");
      {
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
      }
      break;
    case MenuChannels.EDIT_CLEAR_ALL:
      console.log("[renderer] Menu command: edit-clear-all");
      if (window.confirm("Remove all voxels from the model?")) {
        void executeCommand("clear_model", {});
      }
      break;

    // ── View menu (scene operations) ──
    case MenuChannels.VIEW_FRONT:
      try { scene.snapCameraToView("front"); } catch (e) { console.warn("[menu] view-front:", e); }
      break;
    case MenuChannels.VIEW_SIDE:
      try { scene.snapCameraToView("side"); } catch (e) { console.warn("[menu] view-side:", e); }
      break;
    case MenuChannels.VIEW_TOP:
      try { scene.snapCameraToView("top"); } catch (e) { console.warn("[menu] view-top:", e); }
      break;
    case MenuChannels.VIEW_WIREFRAME:
      wireframeVisible = !wireframeVisible;
      scene.setWireframeVisible(wireframeVisible);
      ui.wireframeToggleButton.classList.toggle("active", wireframeVisible);
      break;
    case MenuChannels.VIEW_GRID_SIZE:
      {
        const sizeStr = window.prompt("Grid Size (1-256):", "32");
        const size = parseInt(sizeStr ?? "", 10);
        if (!isNaN(size) && size >= 1 && size <= 256) {
          void executeCommand("set_grid_hint", { size });
        }
      }
      break;

    // ── Reference Model menu ──
    case MenuChannels.REFERENCE_MODEL_LOAD:
      void handleReferenceModelLoad(deps);
      break;
    case MenuChannels.REFERENCE_MODEL_LIST:
      void myraExecuteCommand("List Ref Models", "reflist", []);
      break;
    case MenuChannels.REFERENCE_MODEL_REMOVE:
      {
        const idx = promptInt("Remove Reference Model — enter index:");
        if (idx !== null) {
          void myraExecuteCommand("Remove Ref Model", "refremove", [String(idx)]);
        }
      }
      break;

    // ── Other channels pass through as menu:* IPC events ──
    case MenuChannels.HELP_ABOUT:
      setStatus("About VoxelForge — Voxel Authoring Tool with LLM integration.");
      break;
    case MenuChannels.COMMAND_PALETTE:
      commandPalette?.show();
      break;
    default:
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
    const hit = scene.raycast(event.clientX, event.clientY);
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

/** Prompt for a file path using the browser prompt pattern. */
function promptPath(label: string, placeholder = ""): string | null {
  return window.prompt(`${label}\n\nEnter file path:`, placeholder);
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
