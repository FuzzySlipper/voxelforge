/**
 * Electron renderer process entry point for the VoxelForge workbench.
 * Owns presentation, camera, and UI rendering only. All semantic state changes
 * route through Electron main -> den-bridge -> C# sidecar commands/state.
 */

import { VoxelForgeScene, type MeshSnapshotData, type MeshUpdateEventData, type PaletteUpdateEventData, type RendererMetrics, type VoxelRaycastHit } from "./scene";
import { titleCase, formatError, escapeHtml } from "../shared/string-utils";
import { createCoalescer } from "../shared/refresh-coalescer";

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
    // Confirm in the renderer before clearing
    if (window.confirm("Clear the current model? Unsaved changes will be lost.")) {
      void runAction("New Project", "bridge:project-new", {});
    }
  });

  window.voxelforgeBridge.onEvent("menu:file-open", () => {
    const path = window.prompt("Enter project file path (.vforge):", ui.projectPath.value);
    if (path) {
      ui.projectPath.value = path;
      void runAction("Open", "bridge:project-load", { path });
    }
  });

  window.voxelforgeBridge.onEvent("menu:file-save", () => {
    void runAction("Save", "bridge:project-save", { path: ui.projectPath.value });
  });

  window.voxelforgeBridge.onEvent("menu:file-save-as", () => {
    const path = window.prompt("Save project as (.vforge):", ui.projectPath.value);
    if (path) {
      const fullPath = path.endsWith(".vforge") ? path : path + ".vforge";
      ui.projectPath.value = fullPath;
      void runAction("Save As", "bridge:project-save", { path: fullPath });
    }
  });

  window.voxelforgeBridge.onEvent("menu:file-exit", () => {
    // Main process already closes the window; we just log
    console.log("[renderer] Exit requested via native menu.");
  });

  // ── Edit menu ──
  window.voxelforgeBridge.onEvent("menu:edit-undo", () => {
    void runAction("Undo", "bridge:history-undo", {});
  });

  window.voxelforgeBridge.onEvent("menu:edit-redo", () => {
    void runAction("Redo", "bridge:history-redo", {});
  });

  window.voxelforgeBridge.onEvent("menu:edit-fill-region", () => {
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
    }
  });

  window.voxelforgeBridge.onEvent("menu:edit-palette-list", () => {
    void executeCommand("list_palette", {});
  });

  window.voxelforgeBridge.onEvent("menu:edit-palette-add", () => {
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
    void executeCommand("list_regions", {});
  });

  window.voxelforgeBridge.onEvent("menu:edit-regions-label", () => {
    const regionName = window.prompt("Label Voxel — Region Name:");
    const x = parseInt(window.prompt("Label Voxel — X:") ?? "", 10);
    const y = parseInt(window.prompt("Label Voxel — Y:") ?? "", 10);
    const z = parseInt(window.prompt("Label Voxel — Z:") ?? "", 10);
    if (regionName && !isNaN(x) && !isNaN(y) && !isNaN(z)) {
      void executeCommand("assign_voxels_to_region", {
        region_name: regionName, x, y, z,
      });
    }
  });

  window.voxelforgeBridge.onEvent("menu:edit-clear-all", () => {
    if (window.confirm("Remove all voxels from the model?")) {
      void executeCommand("clear_model", {});
    }
  });

  // ── View menu ──
  window.voxelforgeBridge.onEvent("menu:view-front", () => {
    scene.snapCameraToView("front");
  });

  window.voxelforgeBridge.onEvent("menu:view-side", () => {
    scene.snapCameraToView("side");
  });

  window.voxelforgeBridge.onEvent("menu:view-top", () => {
    scene.snapCameraToView("top");
  });

  window.voxelforgeBridge.onEvent("menu:view-wireframe", () => {
    wireframeVisible = !wireframeVisible;
    scene.setWireframeVisible(wireframeVisible);
    ui.wireframeToggleButton.classList.toggle("active", wireframeVisible);
  });

  window.voxelforgeBridge.onEvent("menu:view-grid-size", () => {
    const sizeStr = window.prompt("Grid Size (1-256):", "32");
    const size = parseInt(sizeStr ?? "", 10);
    if (!isNaN(size) && size >= 1 && size <= 256) {
      void executeCommand("set_grid_hint", { size });
    }
  });

  window.voxelforgeBridge.onEvent("menu:view-measure-grid", () => {
    void executeCommand("toggle_measure_grid", {});
  });

  window.voxelforgeBridge.onEvent("menu:view-measure-scale", () => {
    const scaleStr = window.prompt("Voxels per meter (e.g. 8):", "8");
    const scale = parseFloat(scaleStr ?? "");
    if (!isNaN(scale) && scale > 0) {
      void executeCommand("set_measure_scale", { voxels_per_meter: scale });
    }
  });

  window.voxelforgeBridge.onEvent("menu:view-bg-color", () => {
    const r = parseInt(window.prompt("Background Color — R (0-255):", "43") ?? "43", 10);
    const g = parseInt(window.prompt("Background Color — G (0-255):", "43") ?? "43", 10);
    const b = parseInt(window.prompt("Background Color — B (0-255):", "43") ?? "43", 10);
    if (!isNaN(r) && !isNaN(g) && !isNaN(b)) {
      scene.setBackgroundColor(Math.max(0, Math.min(255, r)), Math.max(0, Math.min(255, g)), Math.max(0, Math.min(255, b)));
    }
  });

  // ── Help menu ──
  window.voxelforgeBridge.onEvent("menu:help-about", () => {
    setStatus("About VoxelForge — Voxel Authoring Tool with LLM integration. Coordinate System: Y-up (right-handed). R=X, G=Y, B=Z.");
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
 * Coalesced mesh refresh: at most one bridge:mesh-snapshot in-flight at a
 * time.  Calls made during an in-flight request are deferred and run once
 * after the current request completes, collapsing multiple overlapping
 * requests into a single final refresh.
 *
 * This prevents cascading timeouts during rapid edits while still keeping
 * the UI eventually current.
 */
const refreshMesh = createCoalescer(async (): Promise<void> => {
  try {
    const meshData = await window.voxelforgeBridge.request("bridge:mesh-snapshot", {
      model_id: "",
      lod_level: 0,
      payload_format: "json",
      include_palette_mapping: true,
    }) as MeshSnapshotData;

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
