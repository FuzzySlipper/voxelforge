/**
 * MCP Viewer main entrypoint.
 * Uses HttpSseRenderClient and VoxelForgeScene from renderer-core to
 * render the VoxelForge model in a browser environment served from the MCP host.
 *
 * This module is bundled as viewer-bundle.js and loaded by viewer.html.
 */

import { WebGLRenderer } from "three";
import { VoxelForgeScene, type RendererMetrics } from "../renderer-core/scene/VoxelForgeScene";
import { HttpSseRenderClient } from "../renderer-core/transport/HttpSseRenderClient";
import { captureReadyManager } from "../renderer-core/scene/captureReady";
import type { RenderSceneSnapshot, ViewerSseEvent, RenderStateSummary } from "../renderer-core/protocol/types";

// ── DOM refs ──

function getEl<T extends HTMLElement>(id: string): T {
  const el = document.getElementById(id);
  if (!el) throw new Error(`Missing element #${id}`);
  return el as T;
}

const ui = {
  container: getEl<HTMLElement>("renderer-container"),
  loading: getEl<HTMLElement>("loading"),
  fallback: getEl<HTMLElement>("fallback-msg"),
  diagModel: getEl<HTMLElement>("diag-model"),
  diagMesh: getEl<HTMLElement>("diag-mesh"),
  diagMeshDetail: getEl<HTMLElement>("diag-mesh-detail"),
  diagFps: getEl<HTMLElement>("diag-fps"),
  diagConn: getEl<HTMLElement>("diag-connection"),
  connDot: getEl<HTMLElement>("conn-dot"),
  statusText: getEl<HTMLElement>("status-text"),
  statusRev: getEl<HTMLElement>("status-revision"),
  statusVoxels: getEl<HTMLElement>("status-voxels"),
  statusTris: getEl<HTMLElement>("status-tris"),
};

// ── State ──

let scene: VoxelForgeScene | null = null;
let client: HttpSseRenderClient | null = null;
let lastSnapshot: RenderSceneSnapshot | null = null;
let lastModelId: string | null = null;
let animFrameId: number | null = null;
let pollTimer: number | null = null;
let sseUnsub: (() => void) | null = null;
let lastRevision = -1;
let connected = false;
let smoothedFps = 0;
let frameTimestamps: number[] = [];
let lastFpsUpdate = 0;

// ── Main ──

async function main(): Promise<void> {
  const captureMode = new URLSearchParams(window.location.search).get("capture") === "1";

  client = new HttpSseRenderClient({
    captureMode,
  });

  // Create scene
  try {
    scene = new VoxelForgeScene(ui.container);
  } catch (err) {
    console.error("[mcp-viewer] Failed to create scene:", err);
    ui.fallback.classList.remove("hidden");
    ui.loading.classList.add("hidden");
    return;
  }

  if (!scene.isWebglAvailable) {
    ui.fallback.classList.remove("hidden");
    ui.loading.classList.add("hidden");
    // Still try to fetch state for display
    const state = await client.getRenderState();
    if (state.connected) {
      updateDiagnostics(state);
    }
    return;
  }

  // Register render complete callback
  scene.onRenderComplete((metrics: RendererMetrics) => {
    updateDiagnostics({
      connected: true,
      model_name: lastSnapshot?.model_id ?? "",
      voxel_count: metrics.vertex_count,
      revision: lastRevision,
      reference_model_count: lastSnapshot?.reference_nodes.length ?? 0,
      reference_vertex_count: 0,
      capture_ready: true,
      pending_texture_loads: 0,
    });
    ui.loading.classList.add("hidden");
  });

  // Connect SSE for live updates (not in capture mode)
  if (!captureMode) {
    sseUnsub = client.subscribeEvents(
      (event) => {
        if (event.type === "revision" || event.type === "connected") {
          lastRevision = event.revision;
          fetchAndBuild();
        }
      },
      (err) => {
        console.warn("[mcp-viewer] SSE error:", err);
      },
    );
  }

    // Parse camera params
    const cameraParams = parseCameraParams();
    if (cameraParams && cameraParams.yaw !== null) {
        // Store for later use after first mesh build
        (scene as any).__cameraParams = cameraParams;
    }

    // Initial load
    await fetchAndBuild();
    // Apply camera params after first mesh build if present
    if (cameraParams && cameraParams.yaw !== null && scene) {
        try {
            scene.viewFromAngle(
                cameraParams.yaw,
                cameraParams.pitch ?? 0,
                cameraParams.distance ?? undefined,
            );
            console.log(`[mcp-viewer] Applied camera params: yaw=${cameraParams.yaw}, pitch=${cameraParams.pitch}, distance=${cameraParams.distance}`);
        } catch (err) {
            console.warn('[mcp-viewer] Failed to apply camera params:', err);
        }
    }

    // Start polling (will short-circuit if SSE is active)
    if (!captureMode) {
        pollTimer = window.setInterval(refreshDiagnostics, 3000);
    }

    // In capture mode, scene build and texture loading are tracked
    // automatically via CaptureReadyManager integration in VoxelForgeScene.
    // Register the onReady handler AFTER camera params are applied, so the
    // final render uses the preset camera position, not the default framing.
    // (buildFromSnapshot calls captureReadyManager.onSceneBuildComplete()
    // internally, which could fire early. By deferring registration until
    // after viewFromAngle, we ensure the capture frame has the correct view.)
    if (captureMode) {
        captureReadyManager.onSceneBuildComplete();
        captureReadyManager.onReady(() => {
            if (animFrameId === null && scene) {
                renderOnce();
            }
        });
    }

  ui.loading.classList.add("hidden");
}

// ── Fetch and build ──

async function fetchAndBuild(): Promise<void> {
  if (!client || !scene) return;

  try {
    const snapshot = await client.getRenderSnapshot();

    if (snapshot.revision === lastRevision && lastSnapshot != null) {
      // No changes — just refresh diagnostics
      const state = await client.getRenderState();
      if (state.connected) {
        updateDiagnostics(state);
      }
      return;
    }

    lastRevision = snapshot.revision;
    lastSnapshot = snapshot;
    lastModelId = snapshot.model_id;

    const metrics = scene.buildFromSnapshot(snapshot);

    // Update diagnostics
    updateDiagnostics({
      connected: true,
      model_name: snapshot.model_id,
      voxel_count: metrics.vertex_count,
      revision: snapshot.revision,
      reference_model_count: snapshot.reference_nodes.length,
      reference_vertex_count: snapshot.reference_nodes.reduce(
        (s, n) => s + n.primitives.reduce((ps, p) => ps + p.position.length / 3, 0), 0,
      ),
      capture_ready: true,
      pending_texture_loads: 0,
    });

    // Start animation loop for non-capture mode
    if (animFrameId === null && !new URLSearchParams(window.location.search).get("capture")) {
      startAnimation();
    } else {
      renderOnce();
    }
  } catch (err) {
    console.warn("[mcp-viewer] fetchAndBuild error:", err);
  }
}

async function refreshDiagnostics(): Promise<void> {
  if (!client) return;
  const state = await client.getRenderState();
  if (state.connected) {
    connected = true;
    updateDiagnostics(state);
  } else {
    connected = false;
    updateDiagnostics({
      connected: false,
      model_name: "\u2014",
      voxel_count: 0,
      revision: 0,
      reference_model_count: 0,
      reference_vertex_count: 0,
      capture_ready: false,
      pending_texture_loads: 0,
    });
  }
}

// ── Animation ──

function startAnimation(): void {
  if (!scene) return;
  animFrameId = requestAnimationFrame(animate);
}

function animate(): void {
  if (!scene || !scene.getRenderer()) return;
  animFrameId = requestAnimationFrame(animate);

  const controls = scene.getControls();
  if (controls) controls.update();

  // Track FPS
  const now = performance.now();
  frameTimestamps.push(now);
  if (frameTimestamps.length > 64) frameTimestamps.shift();

  scene.getRenderer()!.render(scene.getScene(), scene.getCamera());

  // Update FPS display at most every 200ms
  if (ui.diagFps && now - lastFpsUpdate >= 200) {
    updateFps(now);
    lastFpsUpdate = now;
  }
}

function renderOnce(): void {
  if (!scene) return;
  const r = scene.getRenderer();
  if (!r) return;
  const renderer = r as WebGLRenderer;
  renderer.render(scene.getScene(), scene.getCamera());
}

function updateFps(now: number): void {
  while (frameTimestamps.length > 0 && frameTimestamps[0] < now - 1000) {
    frameTimestamps.shift();
  }
  const instantFps = frameTimestamps.length;
  smoothedFps = smoothedFps === 0
    ? instantFps
    : smoothedFps * 0.85 + instantFps * 0.15;

  const fps = Math.round(smoothedFps);
  let inner = ui.diagFps.querySelector(".fps-value");
  if (!inner) {
    ui.diagFps.innerHTML = 'FPS: <span class="fps-value"></span>';
    inner = ui.diagFps.querySelector(".fps-value");
  }
  inner!.textContent = String(fps);

  ui.diagFps.className = "diag-pill " + (
    fps >= 50 ? "perf-ok" :
    fps >= 25 ? "perf-warn" :
    fps >= 1  ? "perf-bad" :
    "perf"
  );
}

// ── Diagnostics ──

function updateDiagnostics(state: RenderStateSummary): void {
  const isUp = state.connected;
  let refInfo = "";
  if (state.reference_model_count > 0) {
    refInfo = ` \u00b7 ref ${state.reference_model_count} models (${state.reference_vertex_count} verts)`;
  }
  ui.diagModel.textContent = `Model: ${state.model_name} (${state.voxel_count} voxels${refInfo})`;
  ui.diagConn.textContent = isUp ? `Connected \u00b7 rev ${state.revision}` : "Disconnected";
  ui.diagConn.className = `diag-pill ${isUp ? "ok" : "warn"}`;
  ui.connDot.className = `dot ${isUp ? "ok" : "warn"}`;
  ui.statusText.textContent = isUp ? "Connected" : "Disconnected \u2014 retrying\u2026";
  ui.statusRev.textContent = `rev ${state.revision}`;

  let voxelStr = `${state.voxel_count} voxels`;
  if (state.reference_model_count > 0) {
    voxelStr += ` \u00b7 ${state.reference_model_count} ref`;
  }
  ui.statusVoxels.textContent = voxelStr;
}

// ── Camera params ──

function parseCameraParams(): { yaw: number | null; pitch: number | null; distance: number | null } {
  const params = new URLSearchParams(window.location.search);
  let yaw = parseFloat(params.get("yaw") ?? "");
  let pitch = parseFloat(params.get("pitch") ?? "");
  let distance = parseFloat(params.get("distance") ?? "");

  const preset = params.get("preset");
  if (preset) {
    switch (preset) {
      case "front":   yaw = 0;        pitch = 0;        break;
      case "right":   yaw = 1.5708;   pitch = 0;        break;
      case "back":    yaw = 3.14159;  pitch = 0;        break;
      case "top":     yaw = 0;        pitch = 1.5708;   break;
      case "isometric": yaw = 0.7854; pitch = 0.6155;   break;
    }
  }
  if (isNaN(yaw))   yaw = null as unknown as number;
  if (isNaN(pitch)) pitch = null as unknown as number;
  if (isNaN(distance) || distance <= 0) distance = null as unknown as number;
  return { yaw, pitch, distance } as { yaw: number | null; pitch: number | null; distance: number | null };
}

// ── Cleanup ──

function dispose(): void {
  if (pollTimer !== null) { clearInterval(pollTimer); pollTimer = null; }
  if (animFrameId !== null) { cancelAnimationFrame(animFrameId); animFrameId = null; }
  if (sseUnsub) { sseUnsub(); sseUnsub = null; }
  if (client) { client.dispose(); client = null; }
  if (scene) { scene.dispose(); scene = null; }
}

// ── Start ──

if (document.readyState === "loading") {
  document.addEventListener("DOMContentLoaded", main);
} else {
  main().catch((err) => {
    console.error("[mcp-viewer] Unhandled error:", err);
    ui.statusText.textContent = `Error: ${err.message ?? String(err)}`;
    ui.statusText.className = "status-err";
    ui.loading.classList.add("hidden");
  });
}

// Expose for dev console
(window as any).__voxelforgeViewer = {
  fetchAndBuild,
  refreshDiagnostics,
  dispose,
  fps: () => Math.round(smoothedFps),
};
