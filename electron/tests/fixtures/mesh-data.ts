import type { MeshSnapshotData } from "../../src/renderer/scene";

/**
 * A simple 2x2x2 cube mesh snapshot fixture.
 * Vertices: 8 corners with 2 triangles per face (12 triangles, 36 indices).
 * Colors are RGBA byte values representing palette index 1 (gray) and index 2 (red).
 */
export const cubeMeshSnapshot: MeshSnapshotData = {
  model_id: "test-model",
  mesh_id: "cube-001",
  format: "json",
  vertex_count: 8,
  index_count: 36,
  triangle_count: 12,
  positions: [
    // Front face (z = 1)
    -1, -1, 1, 1, -1, 1, 1, 1, 1, -1, 1, 1,
    // Back face (z = -1)
    -1, -1, -1, 1, -1, -1, 1, 1, -1, -1, 1, -1,
  ],
  normals: [
    // Front face
    0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1,
    // Back face
    0, 0, -1, 0, 0, -1, 0, 0, -1, 0, 0, -1,
  ],
  // RGBA bytes: [r,g,b,a] per vertex, palette index 1 = gray (128,128,128,255),
  // index 2 = red (255,0,0,255)
  colors: [128, 128, 128, 255, 128, 128, 128, 255, 128, 128, 128, 255, 128, 128, 128, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255],
  indices: [
    // Front face (2 triangles)
    0, 1, 2, 0, 2, 3,
    // Back face
    4, 6, 5, 4, 7, 6,
  ],
  bounds: {
    min_x: -1,
    min_y: -1,
    min_z: -1,
    max_x: 1,
    max_y: 1,
    max_z: 1,
  },
  palette_mapping: {
    "1": { name: "Stone", color: "#808080", a: 255, visible: true },
    "2": { name: "Brick", color: "#ff0000", a: 255, visible: true },
  },
  metrics: {
    mesh_generation_ms: 0.5,
    serialization_ms: 0.2,
    total_ms: 0.7,
  },
};

/**
 * An empty / no-vertex mesh snapshot.
 */
export const emptyMeshSnapshot: MeshSnapshotData = {
  model_id: "test-model",
  mesh_id: "empty-001",
  format: "json",
  vertex_count: 0,
  index_count: 0,
  triangle_count: 0,
  positions: [],
  normals: [],
  colors: [],
  indices: [],
};

/**
 * A mesh snapshot where colors arrive as base64-encoded strings,
 * simulating C# System.Text.Json serialization of byte[].
 * Same data as cubeMeshSnapshot but with base64-encoded colors.
 */
export const base64ColorsCubeSnapshot: MeshSnapshotData = {
  ...cubeMeshSnapshot,
  mesh_id: "cube-base64-001",
  colors: btoa(
    String.fromCharCode(
      128, 128, 128, 255, 128, 128, 128, 255, 128, 128, 128, 255, 128, 128, 128, 255,
      255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255,
    ),
  ),
};

/**
 * A single-cube voxel mesh snapshot (1x1x1, 8 vertices, 12 triangles).
 * Centered at origin, unit cube.
 */
export const unitVoxelMeshSnapshot: MeshSnapshotData = {
  model_id: "test-model",
  mesh_id: "voxel-001",
  format: "json",
  vertex_count: 8,
  index_count: 36,
  triangle_count: 12,
  positions: [
    -0.5, -0.5, 0.5, 0.5, -0.5, 0.5, 0.5, 0.5, 0.5, -0.5, 0.5, 0.5,
    -0.5, -0.5, -0.5, 0.5, -0.5, -0.5, 0.5, 0.5, -0.5, -0.5, 0.5, -0.5,
  ],
  normals: [
    0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1,
    0, 0, -1, 0, 0, -1, 0, 0, -1, 0, 0, -1,
  ],
  colors: [128, 128, 128, 255, 128, 128, 128, 255, 128, 128, 128, 255, 128, 128, 128, 255, 128, 128, 128, 255, 128, 128, 128, 255, 128, 128, 128, 255, 128, 128, 128, 255],
  indices: [
    0, 1, 2, 0, 2, 3,
    4, 6, 5, 4, 7, 6,
  ],
  bounds: {
    min_x: -0.5,
    min_y: -0.5,
    min_z: -0.5,
    max_x: 0.5,
    max_y: 0.5,
    max_z: 0.5,
  },
};
