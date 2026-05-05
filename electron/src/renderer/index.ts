/**
 * Electron renderer process entry point for VoxelForge static mesh viewer.
 * Owns only rendering and presentation — no model mutations.
 */

import { VoxelForgeScene, type MeshSnapshotData, type RendererMetrics } from "./scene";

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

async function main(): Promise<void> {
  const container = document.getElementById("renderer-container");
  if (!container) {
    console.error("[renderer] No #renderer-container element found");
    return;
  }

  // Show loading state
  container.innerHTML = '<div style="color: #aaa; padding: 20px; font-family: monospace;">Loading VoxelForge mesh data...</div>';

  const scene = new VoxelForgeScene(container);

  scene.onRenderComplete((metrics: RendererMetrics) => {
    console.log("[renderer] Render complete:", metrics);
    // Send metrics back to main process for logging/smoke tests
    if (window.voxelforgeBridge) {
      window.voxelforgeBridge.sendMetrics({
        scene_construction_ms: metrics.scene_construction_ms,
        first_render_ms: metrics.first_render_ms,
        vertex_count: metrics.vertex_count,
        triangle_count: metrics.triangle_count,
        total_renderer_ms: metrics.total_renderer_ms,
      });
    }
  });

  try {
    // 1. Perform VoxelForge schema handshake
    if (window.voxelforgeBridge) {
      const handshake = await window.voxelforgeBridge.request("bridge:handshake", {
        client_schema_version: "voxelforge@1",
        supported_capabilities: ["mesh_json"],
      }) as { sidecar_schema_version?: string; compatible?: boolean; supported_capabilities?: string[] };

      console.log("[renderer] Schema handshake:", handshake);
      if (!handshake.compatible) {
        container.innerHTML = `<div style="color: #f55; padding: 20px; font-family: monospace;">
          Incompatible VoxelForge schema version: ${handshake.sidecar_schema_version}
        </div>`;
        return;
      }
    }

    // 2. Request mesh snapshot from the C# sidecar
    let meshData: MeshSnapshotData;
    if (window.voxelforgeBridge) {
      meshData = await window.voxelforgeBridge.request("bridge:mesh-snapshot", {
        model_id: "",
        lod_level: 0,
        payload_format: "json",
        include_palette_mapping: true,
      }) as MeshSnapshotData;
    } else {
      // No bridge available — show a placeholder cube for dev testing
      meshData = createFallbackCube();
    }

    console.log("[renderer] Mesh data received:", {
      model_id: meshData.model_id,
      vertices: meshData.vertex_count,
      triangles: meshData.triangle_count,
      bounds: meshData.bounds,
    });

    // 3. Build Three.js mesh from snapshot data
    const metrics = scene.buildMeshFromSnapshot(meshData);
    console.log("[renderer] Scene constructed:", metrics);

    // 4. Update status
    const statusEl = document.getElementById("status");
    if (statusEl) {
      statusEl.textContent =
        `Model: ${meshData.model_id} | Vertices: ${meshData.vertex_count} | Triangles: ${meshData.triangle_count}`;
    }

    // 5. Subscribe to mesh update events for incremental updates
    if (window.voxelforgeBridge) {
      setupMeshSubscription(meshData as MeshSnapshotData);
    }
  } catch (err) {
    console.error("[renderer] Error:", err);
    container.innerHTML = `<div style="color: #f55; padding: 20px; font-family: monospace;">
      Error: ${err}
    </div>`;
  }

  // Notify main process that renderer is ready
  if (window.voxelforgeBridge) {
    window.voxelforgeBridge.notifyReady();
  }
}

/**
 * Create a fallback cube for development when no bridge is available.
 * This allows testing the Three.js scene construction without the C# sidecar.
 */
function createFallbackCube(): MeshSnapshotData {
  // Simple 2x2x2 cube: 8 vertices, 12 triangles
  const positions = [
    // Front face
    0, 0, 2, 2, 0, 2, 2, 2, 2, 0, 2, 2,
    // Back face
    0, 0, 0, 0, 2, 0, 2, 2, 0, 2, 0, 0,
    // Top face
    0, 2, 0, 0, 2, 2, 2, 2, 2, 2, 2, 0,
    // Bottom face
    0, 0, 0, 2, 0, 0, 2, 0, 2, 0, 0, 2,
    // Right face
    2, 0, 0, 2, 2, 0, 2, 2, 2, 2, 0, 2,
    // Left face
    0, 0, 0, 0, 0, 2, 0, 2, 2, 0, 2, 0,
  ];

  const normals = [
    // Front (z+)
    0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1,
    // Back (z-)
    0, 0, -1, 0, 0, -1, 0, 0, -1, 0, 0, -1,
    // Top (y+)
    0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1, 0,
    // Bottom (y-)
    0, -1, 0, 0, -1, 0, 0, -1, 0, 0, -1, 0,
    // Right (x+)
    1, 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0,
    // Left (x-)
    -1, 0, 0, -1, 0, 0, -1, 0, 0, -1, 0, 0,
  ];

  // RGBA colors: warm orange-brown
  const colors = new Array(24 * 4).fill(0);
  for (let i = 0; i < 24; i++) {
    colors[i * 4] = 180;     // R
    colors[i * 4 + 1] = 120; // G
    colors[i * 4 + 2] = 80;  // B
    colors[i * 4 + 3] = 255; // A
  }

  const indices = [
    0, 1, 2, 0, 2, 3,       // front
    4, 5, 6, 4, 6, 7,       // back
    8, 9, 10, 8, 10, 11,    // top
    12, 13, 14, 12, 14, 15, // bottom
    16, 17, 18, 16, 18, 19, // right
    20, 21, 22, 20, 22, 23, // left
  ];

  return {
    model_id: "fallback-cube",
    mesh_id: "mesh-fallback-00000000000000",
    format: "json",
    vertex_count: 24,
    index_count: 36,
    triangle_count: 12,
    positions,
    normals,
    colors,
    indices,
    bounds: {
      min_x: 0, min_y: 0, min_z: 0,
      max_x: 2, max_y: 2, max_z: 2,
    },
    palette_mapping: {
      "1": { name: "Stone", color: "#B47850", a: 255, visible: true },
    },
  };
}

/**
 * Subscribe to incremental mesh update events from the C# sidecar.
 * When mesh updates arrive, apply them to the scene using incremental buffer replacement.
 */
function setupMeshSubscription(initialSnapshot: MeshSnapshotData): void {
  let currentMeshId = initialSnapshot.mesh_id;

  // Subscribe to mesh update events
  if (window.voxelforgeBridge) {
    window.voxelforgeBridge.onEvent("voxelforge:mesh-update", (payload: unknown) => {
      const update = payload as MeshUpdateEventData;
      console.log("[renderer] Mesh update received:", {
        model_id: update.model_id,
        sequence: update.sequence,
        update_type: update.update_type,
        regions: update.changed_regions.length,
      });

      // Check if the update applies to our current mesh
      if (update.base_mesh_id !== currentMeshId && update.update_type !== "full_replace") {
        console.warn(
          `[renderer] Mesh update base mismatch: expected ${currentMeshId}, got ${update.base_mesh_id}. Requesting full snapshot.`,
        );
        // Out of sync — request a full snapshot to resync
        requestFullMeshSnapshot();
        return;
      }

      try {
        const metrics = scene.applyIncrementalUpdate(update);
        currentMeshId = update.base_mesh_id;
        console.log("[renderer] Incremental mesh update applied:", metrics);

        // Update status
        const statusEl = document.getElementById("status");
        if (statusEl) {
          statusEl.textContent =
            `Model: ${update.model_id} | Vertices: ${update.full_vertex_count} | Update: ${update.update_type} | Regions: ${update.changed_regions.length}`;
        }
      } catch (err) {
        console.error("[renderer] Failed to apply incremental mesh update:", err);
      }
    });

    // Subscribe to palette update events
    window.voxelforgeBridge.onEvent("voxelforge:palette-update", (payload: unknown) => {
      const update = payload as PaletteUpdateEventData;
      console.log("[renderer] Palette update received:", {
        model_id: update.model_id,
        sequence: update.sequence,
        update_type: update.update_type,
        entries: update.entry_count,
      });
    });

    // Send mesh subscription request
    window.voxelforgeBridge.request("bridge:mesh-subscribe", {
      model_id: "",
      chunk_size: 16,
      send_full_snapshot_on_subscribe: false,
    }).catch((err: unknown) => {
      console.warn("[renderer] Mesh subscription failed (non-fatal for static renderer):", err);
    });
  }
}

async function requestFullMeshSnapshot(): Promise<void> {
  if (!window.voxelforgeBridge) return;
  try {
    const meshData = await window.voxelforgeBridge.request("bridge:mesh-snapshot", {
      model_id: "",
      lod_level: 0,
      payload_format: "json",
      include_palette_mapping: true,
    }) as MeshSnapshotData;
    const metrics = scene.buildMeshFromSnapshot(meshData);
    console.log("[renderer] Full mesh resync applied:", metrics);
  } catch (err) {
    console.error("[renderer] Failed to resync mesh:", err);
  }
}

main().catch((err) => console.error("[renderer] Unhandled error:", err));