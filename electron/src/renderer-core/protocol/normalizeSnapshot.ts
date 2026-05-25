/**
 * Snapshot normalization helpers for RenderSceneSnapshot (snake_case #1657 contract).
 *
 * Handles:
 * - Decoding base64 color/palette byte arrays from C# System.Text.Json
 * - Normalizing bounds/materials/texture slots
 * - Provide fallback values for optional fields
 * - Extract RenderStateSummary from snapshot or transitional endpoints
 */

import type {
  RenderSceneSnapshot,
  RenderVoxelMesh,
  RenderPrimitive,
  RenderTextureSlot,
  BoundsDto,
  RenderStateSummary,
  TransitionalViewerState,
  TransitionalMeshSnapshot,
} from "./types";
import { decodeByteArray } from "../../shared/byte-utils";

/**
 * Normalize a RenderSceneSnapshot: decode base64 byte arrays in voxel meshes
 * and reference primitives so all color_rgba/palette_indices fields are
 * number[] arrays, ready for Three.js consumption.
 */
export function normalizeSnapshot(snapshot: RenderSceneSnapshot): RenderSceneSnapshot {
  return {
    ...snapshot,
    voxel_meshes: snapshot.voxel_meshes.map(normalizeVoxelMesh),
    reference_nodes: snapshot.reference_nodes.map((node) => ({
      ...node,
      primitives: node.primitives.map(normalizePrimitive),
    })),
    materials: snapshot.materials.map((mat) => ({
      ...mat,
      diagnostics: mat.diagnostics ?? [],
    })),
    textures: snapshot.textures.map((tex) => ({
      ...tex,
      diagnostics: tex.diagnostics ?? [],
    })),
    palette: snapshot.palette ?? [],
    diagnostics: snapshot.diagnostics ?? [],
  };
}

function normalizeVoxelMesh(mesh: RenderVoxelMesh): RenderVoxelMesh {
  const vertexCount = mesh.positions.length / 3;
  return {
    ...mesh,
    colors_rgba: normalizeByteArrayField(mesh.colors_rgba, vertexCount * 4),
    palette_indices: normalizeByteArrayField(mesh.palette_indices, vertexCount),
  };
}

function normalizePrimitive(prim: RenderPrimitive): RenderPrimitive {
  const vertexCount = prim.position.length / 3;
  return {
    ...prim,
    color_rgba: prim.color_rgba != null
      ? normalizeByteArrayField(prim.color_rgba, vertexCount * 4)
      : null,
    uv_sets: (prim.uv_sets ?? []).map((uv) => ({
      ...uv,
      origin: uv.origin ?? "unknown",
      flip_y: uv.flip_y ?? "asset_defined",
    })),
  };
}

/**
 * Accept JSON number[] or base64 string, always return number[].
 * Uses the shared decodeByteArray from byte-utils.
 */
export function normalizeByteArrayField(
  value: number[] | string | null | undefined,
  expectedLength?: number,
): number[] {
  if (value == null) return [];
  if (typeof value === "string") {
    // Base64-encoded byte array from C# System.Text.Json
    return decodeByteArray(value, expectedLength);
  }
  // Already a number array
  return value;
}

/**
 * Normalize optional bounds to a consistent shape.
 */
export function normalizeBounds(
  bounds: BoundsDto | null | undefined,
): BoundsDto | null {
  if (!bounds) return null;
  return {
    min_x: bounds.min_x ?? 0,
    min_y: bounds.min_y ?? 0,
    min_z: bounds.min_z ?? 0,
    max_x: bounds.max_x ?? 0,
    max_y: bounds.max_y ?? 0,
    max_z: bounds.max_z ?? 0,
  };
}

/**
 * Compute combined bounding box from two optional bounds + reference bounds.
 */
export function computeCombinedBounds(
  voxelBounds: BoundsDto | null,
  referenceBounds: BoundsDto | null,
): BoundsDto | null {
  if (!voxelBounds && !referenceBounds) return null;
  if (!voxelBounds) return referenceBounds;
  if (!referenceBounds) return voxelBounds;

  return {
    min_x: Math.min(voxelBounds.min_x, referenceBounds.min_x),
    min_y: Math.min(voxelBounds.min_y, referenceBounds.min_y),
    min_z: Math.min(voxelBounds.min_z, referenceBounds.min_z),
    max_x: Math.max(voxelBounds.max_x, referenceBounds.max_x),
    max_y: Math.max(voxelBounds.max_y, referenceBounds.max_y),
    max_z: Math.max(voxelBounds.max_z, referenceBounds.max_z),
  };
}

/**
 * Extract RenderStateSummary from a full RenderSceneSnapshot.
 */
export function snapshotToStateSummary(
  snapshot: RenderSceneSnapshot,
  overrides?: Partial<RenderStateSummary>,
): RenderStateSummary {
  const totalVerts = snapshot.voxel_meshes.reduce(
    (sum, m) => sum + m.positions.length / 3,
    0,
  );
  const totalTris = snapshot.voxel_meshes.reduce(
    (sum, m) => sum + m.indices.length / 3,
    0,
  );

  return {
    connected: true,
    model_name: snapshot.model_id,
    voxel_count: totalVerts,
    revision: snapshot.revision,
    reference_model_count: snapshot.reference_nodes.length,
    reference_vertex_count: snapshot.reference_nodes.reduce(
      (sum, n) =>
        sum +
        n.primitives.reduce(
          (ps, p) => ps + p.position.length / 3,
          0,
        ),
      0,
    ),
    capture_ready: false,
    pending_texture_loads: 0,
    ...overrides,
  };
}

/**
 * Convert a transitional /api/viewer-state response to RenderStateSummary.
 */
export function transitionalStateToSummary(
  state: TransitionalViewerState,
): RenderStateSummary {
  return {
    connected: true,
    model_name: state.model_name,
    voxel_count: state.voxel_count,
    revision: state.revision,
    reference_model_count: state.reference_model_count ?? 0,
    reference_vertex_count: state.reference_vertex_count ?? 0,
    capture_ready: false,
    pending_texture_loads: 0,
  };
}

/**
 * Convert a transitional /api/mesh-snapshot response to a minimal
 * RenderSceneSnapshot-like shape for renderer-core consumption.
 * Bridges the old flat API to the new contract.
 */
export function transitionalMeshToSnapshot(
  mesh: TransitionalMeshSnapshot,
  state?: TransitionalViewerState | null,
): RenderSceneSnapshot {
  return {
    schema_version: "voxelforge.render_scene@transitional@1",
    revision: state?.revision ?? 0,
    model_id: mesh.model_id,
    source: {
      host: "mcp",
      capabilities: ["voxel_mesh", "palette", "reference"],
    },
    bounds: normalizeBounds(mesh.bounds),
    reference_bounds: null, // Not directly available in transitional API
    combined_bounds: mesh.combined_bounds
      ? normalizeBounds(mesh.combined_bounds)
      : null,
    voxel_meshes: [
      {
        id: mesh.mesh_id,
        revision: 0,
        positions: mesh.positions ?? [],
        normals: mesh.normals ?? [],
        colors_rgba: normalizeByteArrayField(mesh.colors),
        palette_indices: [],
        indices: mesh.indices ?? [],
        bounds: normalizeBounds(mesh.bounds),
        payload_format: mesh.format ?? "json_arrays",
      },
    ],
    reference_nodes: buildTransitionalReferenceNodes(mesh),
    materials: [],
    textures: [],
    palette: buildTransitionalPalette(mesh),
    diagnostics: [],
  };
}

function buildTransitionalReferenceNodes(
  mesh: TransitionalMeshSnapshot,
): RenderSceneSnapshot["reference_nodes"] {
  if (!mesh.reference_models || mesh.reference_models.length === 0) return [];

  return mesh.reference_models.map((rm, idx) => ({
    id: `ref-${idx}`,
    display_name: rm.file_name ?? `ref-${idx}`,
    source_format: rm.format ?? "unknown",
    source_asset_id: null,
    visible: rm.is_visible ?? true,
    render_mode: "textured",
    transform: {
      position_x: rm.position_x ?? 0,
      position_y: rm.position_y ?? 0,
      position_z: rm.position_z ?? 0,
      rotation_x: rm.rotation_x ?? 0,
      rotation_y: rm.rotation_y ?? 0,
      rotation_z: rm.rotation_z ?? 0,
      scale: rm.scale ?? 1,
    },
    bounds_local: rm.bounds ? normalizeBounds(rm.bounds) : null,
    bounds_world: null,
    primitives: buildTransitionalPrimitives(rm, idx),
    diagnostics: [],
  }));
}

function buildTransitionalPrimitives(
  rm: NonNullable<TransitionalMeshSnapshot["reference_models"]>[number],
  modelIndex: number,
): RenderPrimitive[] {
  if (rm.meshes && rm.meshes.length > 0) {
    return rm.meshes.map((m) => ({
      id: `ref-${modelIndex}-mesh-${m.mesh_index}`,
      material_index: m.mesh_index,
      position: m.positions ?? [],
      normal: m.normals ?? [],
      color_rgba: m.colors && m.colors.length > 0
        ? normalizeByteArrayField(m.colors)
        : null,
      uv_sets: m.has_uvs && m.uvs && m.uvs.length > 0
        ? [
            {
              set_index: 0,
              uvs: m.uvs,
              origin: "unknown",
              flip_y: "asset_defined",
            },
          ]
        : [],
      indices: m.indices ?? null,
      bounds_local: null,
    }));
  }

  // Fallback: flattened model-level geometry
  return [
    {
      id: `ref-${modelIndex}-flat`,
      material_index: 0,
      position: rm.positions ?? [],
      normal: rm.normals ?? [],
      color_rgba: rm.colors && rm.colors.length > 0
        ? normalizeByteArrayField(rm.colors)
        : null,
      uv_sets: rm.uvs && rm.uvs.length > 0
        ? [
            {
              set_index: 0,
              uvs: rm.uvs,
              origin: "unknown",
              flip_y: "asset_defined",
            },
          ]
        : [],
      indices: rm.indices ?? null,
      bounds_local: null,
    },
  ];
}

function buildTransitionalPalette(
  mesh: TransitionalMeshSnapshot,
): RenderSceneSnapshot["palette"] {
  if (!mesh.palette_mapping) return [];
  return Object.entries(mesh.palette_mapping).map(([idx, entry]) => {
    const colorHex = entry.color.replace("#", "");
    const r = parseInt(colorHex.substring(0, 2), 16) || 0;
    const g = parseInt(colorHex.substring(2, 4), 16) || 0;
    const b = parseInt(colorHex.substring(4, 6), 16) || 0;
    return {
      index: parseInt(idx, 10),
      name: entry.name,
      r,
      g,
      b,
      a: entry.a,
      visible: entry.visible,
    };
  });
}
