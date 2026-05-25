/**
 * @deprecated This module is a backward-compatibility shim.
 * The canonical Three.js scene manager is now in renderer-core.
 * Direct consumers should import from "../renderer-core" instead.
 *
 * This file re-exports the renderer-core VoxelForgeScene and keeps
 * the original type aliases for backward compatibility.
 */

// Re-export the core scene implementation from renderer-core
export {
  VoxelForgeScene,
  shouldFrameCamera,
  RendererMetrics,
  VoxelForgeSceneOptions,
} from "../renderer-core";

// Re-export shared utilities still used by the renderer shell
export { VoxelRaycastHit } from "../shared/compute-placement";

// ── Backward-compatible type aliases ──

/** @deprecated Use types from renderer-core protocol/types instead. */
export interface MeshSnapshotData {
  model_id: string;
  mesh_id: string;
  format: string;
  vertex_count: number;
  index_count: number;
  triangle_count: number;
  positions: number[];
  normals: number[];
  colors: number[] | string;
  palette_indices?: number[] | string;
  indices: number[];
  bounds?: {
    min_x: number;
    min_y: number;
    min_z: number;
    max_x: number;
    max_y: number;
    max_z: number;
  };
  palette_mapping?: Record<string, { name: string; color: string; a: number; visible: boolean }>;
  metrics?: {
    mesh_generation_ms: number;
    serialization_ms: number;
    total_ms: number;
  };
}

/** @deprecated Use types from renderer-core protocol/types instead. */
export interface PaletteData {
  palette_id: string;
  entries: { index: number; name: string; color: string; a: number; visible: boolean }[];
  entry_count: number;
}

/** @deprecated Use types from renderer-core protocol/types instead. */
export interface MeshUpdateEventData {
  model_id: string;
  base_mesh_id: string;
  sequence: number;
  update_type: "incremental" | "full_replace";
  changed_regions: MeshRegionUpdateData[];
  payload_format: string;
  full_vertex_count: number;
  full_index_count: number;
  metrics?: {
    region_count: number;
    build_ms: number;
    serialize_ms: number;
  };
}

/** @deprecated Use types from renderer-core protocol/types instead. */
export interface MeshRegionUpdateData {
  region_id: string;
  update_kind: "incremental" | "full_replace";
  bounds: { min_x: number; min_y: number; min_z: number; max_x: number; max_y: number; max_z: number };
  vertex_offset: number;
  vertex_count: number;
  index_offset: number;
  index_count: number;
  positions: number[];
  normals: number[];
  colors: number[] | string;
  palette_indices?: number[] | string;
  indices: number[];
}

/** @deprecated Use types from renderer-core protocol/types instead. */
export interface PaletteUpdateEventData {
  model_id: string;
  sequence: number;
  update_type: "full_replace" | "partial";
  entries: { index: number; name: string; color: string; a: number; visible: boolean }[];
  entry_count: number;
}
