/**
 * Tests for protocol/normalizeSnapshot.ts — snapshot normalization,
 * byte array handling, transitional API bridge, and snake_case field mapping.
 */

import { describe, it, expect } from "vitest";
import {
  normalizeSnapshot,
  normalizeByteArrayField,
  normalizeBounds,
  computeCombinedBounds,
  snapshotToStateSummary,
  transitionalStateToSummary,
  transitionalMeshToSnapshot,
} from "../../src/renderer-core/protocol/normalizeSnapshot";
import type { RenderSceneSnapshot, BoundsDto } from "../../src/renderer-core/protocol/types";

// ── Test data ──

const sampleSnapshot: RenderSceneSnapshot = {
  schema_version: "voxelforge.render_scene@1",
  revision: 42,
  model_id: "test-model",
  source: { host: "mcp", capabilities: ["voxel_mesh"] },
  bounds: { min_x: -5, min_y: -5, min_z: -5, max_x: 5, max_y: 5, max_z: 5 },
  reference_bounds: null,
  combined_bounds: { min_x: -10, min_y: -10, min_z: -10, max_x: 10, max_y: 10, max_z: 10 },
  voxel_meshes: [
    {
      id: "mesh-1",
      revision: 1,
      positions: [0, 0, 0, 1, 0, 0, 1, 1, 0],
      normals: [0, 0, 1, 0, 0, 1, 0, 0, 1],
      colors_rgba: [128, 128, 128, 255, 200, 200, 200, 255, 100, 100, 100, 255],
      palette_indices: [1, 1, 1],
      indices: [0, 1, 2],
      bounds: { min_x: 0, min_y: 0, min_z: 0, max_x: 1, max_y: 1, max_z: 1 },
      payload_format: "json_arrays",
    },
  ],
  reference_nodes: [
    {
      id: "ref-node-1",
      display_name: "Test Ref",
      source_format: "obj",
      source_asset_id: null,
      visible: true,
      render_mode: "textured",
      transform: { position_x: 0, position_y: 0, position_z: 0, rotation_x: 0, rotation_y: 0, rotation_z: 0, scale: 1 },
      bounds_local: null,
      bounds_world: null,
      primitives: [
        {
          id: "prim-1",
          material_index: 0,
          position: [0, 0, 0, 1, 0, 0],
          normal: [0, 0, 1, 0, 0, 1],
          color_rgba: null,
          uv_sets: [{ set_index: 0, uvs: [0, 0, 1, 0], origin: "unknown", flip_y: "asset_defined" }],
          indices: [0, 1],
          bounds_local: null,
        },
      ],
      diagnostics: [],
    },
  ],
  materials: [],
  textures: [],
  palette: [{ index: 1, name: "Stone", r: 128, g: 128, b: 128, a: 255, visible: true }],
  diagnostics: [],
};

// ── Tests ──

describe("normalizeSnapshot", () => {
  it("passes through a clean snapshot unchanged (no base64)", () => {
    const result = normalizeSnapshot(sampleSnapshot);
    expect(result.schema_version).toBe("voxelforge.render_scene@1");
    expect(result.revision).toBe(42);
    expect(result.voxel_meshes[0].colors_rgba).toEqual([128, 128, 128, 255, 200, 200, 200, 255, 100, 100, 100, 255]);
  });

  it("decodes base64-encoded colors_rgba in voxel meshes", () => {
    const snapshot: RenderSceneSnapshot = {
      ...sampleSnapshot,
      voxel_meshes: [
        {
          ...sampleSnapshot.voxel_meshes[0],
          colors_rgba: btoa(String.fromCharCode(128, 128, 128, 255, 200, 200, 200, 255)),
        },
      ],
    };
    const result = normalizeSnapshot(snapshot);
    expect(Array.isArray(result.voxel_meshes[0].colors_rgba)).toBe(true);
    const colors = result.voxel_meshes[0].colors_rgba as number[];
    expect(colors.length).toBe(8);
    expect(colors[0]).toBe(128);
    expect(colors[3]).toBe(255);
  });

  it("decodes base64-encoded color_rgba in reference primitives", () => {
    const snapshot: RenderSceneSnapshot = {
      ...sampleSnapshot,
      reference_nodes: [
        {
          ...sampleSnapshot.reference_nodes[0],
          primitives: [
            {
              ...sampleSnapshot.reference_nodes[0].primitives[0],
              color_rgba: btoa(String.fromCharCode(255, 0, 0, 255)),
            },
          ],
        },
      ],
    };
    const result = normalizeSnapshot(snapshot);
    const prim = result.reference_nodes[0].primitives[0];
    expect(Array.isArray(prim.color_rgba)).toBe(true);
    expect((prim.color_rgba as number[])[0]).toBe(255);
  });

  it("handles null color_rgba without error", () => {
    const result = normalizeSnapshot(sampleSnapshot);
    const prim = result.reference_nodes[0].primitives[0];
    expect(prim.color_rgba).toBeNull();
  });

  it("handles empty snapshot gracefully", () => {
    const empty: RenderSceneSnapshot = {
      schema_version: "voxelforge.render_scene@1",
      revision: 0,
      model_id: "",
      source: { host: "test", capabilities: [] },
      bounds: null,
      reference_bounds: null,
      combined_bounds: null,
      voxel_meshes: [],
      reference_nodes: [],
      materials: [],
      textures: [],
      palette: [],
      diagnostics: [],
    };
    const result = normalizeSnapshot(empty);
    expect(result.voxel_meshes).toHaveLength(0);
  });
});

describe("normalizeByteArrayField", () => {
  it("returns empty array for null/undefined", () => {
    expect(normalizeByteArrayField(null)).toEqual([]);
    expect(normalizeByteArrayField(undefined)).toEqual([]);
  });

  it("passes through number arrays directly", () => {
    expect(normalizeByteArrayField([1, 2, 3])).toEqual([1, 2, 3]);
  });

  it("decodes base64 strings", () => {
    const encoded = btoa(String.fromCharCode(10, 20, 30));
    const result = normalizeByteArrayField(encoded, 3);
    expect(result).toEqual([10, 20, 30]);
  });
});

describe("normalizeBounds", () => {
  it("returns null for null/undefined", () => {
    expect(normalizeBounds(null)).toBeNull();
    expect(normalizeBounds(undefined)).toBeNull();
  });

  it("passes through valid bounds", () => {
    const bounds: BoundsDto = { min_x: -1, min_y: -2, min_z: -3, max_x: 1, max_y: 2, max_z: 3 };
    expect(normalizeBounds(bounds)).toEqual(bounds);
  });
});

describe("computeCombinedBounds", () => {
  it("returns null when both null", () => {
    expect(computeCombinedBounds(null, null)).toBeNull();
  });

  it("returns voxel bounds when reference null", () => {
    const vb: BoundsDto = { min_x: -1, min_y: -1, min_z: -1, max_x: 1, max_y: 1, max_z: 1 };
    expect(computeCombinedBounds(vb, null)).toEqual(vb);
  });

  it("returns reference bounds when voxel null", () => {
    const rb: BoundsDto = { min_x: -2, min_y: -2, min_z: -2, max_x: 2, max_y: 2, max_z: 2 };
    expect(computeCombinedBounds(null, rb)).toEqual(rb);
  });

  it("combines both bounds correctly", () => {
    const vb: BoundsDto = { min_x: -1, min_y: -1, min_z: -1, max_x: 1, max_y: 1, max_z: 1 };
    const rb: BoundsDto = { min_x: -5, min_y: 0, min_z: -5, max_x: 5, max_y: 0, max_z: 5 };
    const result = computeCombinedBounds(vb, rb);
    expect(result!.min_x).toBe(-5);
    expect(result!.max_x).toBe(5);
    expect(result!.min_y).toBe(-1);
    expect(result!.max_y).toBe(1);
  });
});

describe("snapshotToStateSummary", () => {
  it("extracts correct summary fields", () => {
    const summary = snapshotToStateSummary(sampleSnapshot);
    expect(summary.connected).toBe(true);
    expect(summary.model_name).toBe("test-model");
    expect(summary.revision).toBe(42);
    expect(summary.reference_model_count).toBe(1);
  });
});

describe("transitionalStateToSummary", () => {
  it("converts transitional state correctly", () => {
    const summary = transitionalStateToSummary({
      model_name: "test",
      voxel_count: 100,
      revision: 5,
      grid_hint: 16,
      reference_model_count: 2,
      reference_vertex_count: 5000,
      palette_entries: [],
      bounds: { min_x: 0, min_y: 0, min_z: 0, max_x: 10, max_y: 10, max_z: 10 },
    });
    expect(summary.model_name).toBe("test");
    expect(summary.voxel_count).toBe(100);
    expect(summary.revision).toBe(5);
  });
});

describe("transitionalMeshToSnapshot", () => {
  it("converts transitional mesh to proper snapshot", () => {
    const snapshot = transitionalMeshToSnapshot({
      model_id: "test",
      mesh_id: "mesh-1",
      format: "json",
      vertex_count: 8,
      index_count: 36,
      triangle_count: 12,
      positions: [0, 0, 0, 1, 0, 0],
      normals: [0, 0, 1, 0, 0, 1],
      colors: [128, 128, 128, 255, 200, 200, 200, 255],
      indices: [0, 1, 2],
      bounds: { min_x: 0, min_y: 0, min_z: 0, max_x: 1, max_y: 1, max_z: 1 },
    });
    expect(snapshot.model_id).toBe("test");
    expect(snapshot.voxel_meshes).toHaveLength(1);
    expect(snapshot.source.host).toBe("mcp");
    expect(snapshot.palette).toEqual([]);
    expect(snapshot.reference_nodes).toEqual([]);
  });

  it("includes reference models from transitional data", () => {
    const snapshot = transitionalMeshToSnapshot({
      model_id: "ref-test",
      mesh_id: "mesh-1",
      format: "json",
      vertex_count: 0,
      index_count: 0,
      triangle_count: 0,
      positions: [],
      normals: [],
      colors: [],
      indices: [],
      reference_models: [
        {
          file_name: "test.obj",
          format: "obj",
          is_visible: true,
          position_x: 0,
          position_y: 0,
          position_z: 0,
          rotation_x: 0,
          rotation_y: 0,
          rotation_z: 0,
          scale: 1,
          index: 0,
          total_vertices: 3,
          total_triangles: 1,
          positions: [0, 0, 0, 1, 0, 0, 1, 1, 0],
          normals: [0, 0, 1, 0, 0, 1, 0, 0, 1],
          colors: [128, 128, 128, 255, 200, 200, 200, 255, 100, 100, 100, 255],
          uvs: [],
          indices: [0, 1, 2],
          bounds: { min_x: 0, min_y: 0, min_z: 0, max_x: 1, max_y: 1, max_z: 1 },
        },
      ],
    });
    expect(snapshot.reference_nodes).toHaveLength(1);
    expect(snapshot.reference_nodes[0].display_name).toBe("test.obj");
  });
});
