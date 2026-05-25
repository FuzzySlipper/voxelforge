/**
 * Tests for reference model UV/material/texture handling in renderer-core.
 */

import { describe, it, expect } from "vitest";
import {
  hasUvs,
  shouldFlipV,
  getNormalizedUvs,
  resolveMaterialProperties,
  diagnoseReferencePrimitive,
  computeBoundsFromPositions,
  UV_ORIGIN_TOP_LEFT,
  UV_ORIGIN_BOTTOM_LEFT,
  FLIP_Y_ASSET_DEFINED,
  FLIP_Y_TRUE,
  FLIP_Y_FALSE,
  ALPHA_MODE_OPAQUE,
  ALPHA_MODE_BLEND,
  ALPHA_MODE_MASK,
} from "../../src/renderer-core/scene/referenceModels";
import type {
  RenderPrimitive,
  RenderMaterial,
} from "../../src/renderer-core/protocol/types";

// ── Test data ──

function makePrimitive(overrides?: Partial<RenderPrimitive>): RenderPrimitive {
  return {
    id: "test-prim",
    material_index: 0,
    position: [0, 0, 0, 1, 0, 0, 1, 1, 0],
    normal: [0, 0, 1, 0, 0, 1, 0, 0, 1],
    color_rgba: null,
    uv_sets: [],
    indices: null,
    bounds_local: null,
    ...overrides,
  };
}

function makeMaterial(overrides?: Partial<RenderMaterial>): RenderMaterial {
  return {
    id: "mat-1",
    name: "Test Material",
    base_color_factor: [1, 1, 1, 1],
    base_color_texture: null,
    normal_texture: null,
    emissive_texture: null,
    emissive_factor: null,
    metallic_factor: 0,
    roughness_factor: 0.5,
    alpha_mode: "opaque",
    alpha_cutoff: null,
    double_sided: false,
    color_space: "srgb",
    diagnostics: [],
    ...overrides,
  };
}

// ── Tests ──

describe("hasUvs", () => {
  it("returns false when no uv_sets", () => {
    expect(hasUvs(makePrimitive())).toBe(false);
  });

  it("returns false when uv_sets is empty array", () => {
    expect(hasUvs(makePrimitive({ uv_sets: [] }))).toBe(false);
  });

  it("returns false when first uv_set has no uvs", () => {
    expect(hasUvs(makePrimitive({
      uv_sets: [{ set_index: 0, uvs: [], origin: "unknown", flip_y: "asset_defined" }],
    }))).toBe(false);
  });

  it("returns true when uv_set has uvs", () => {
    expect(hasUvs(makePrimitive({
      uv_sets: [{ set_index: 0, uvs: [0, 0, 1, 0], origin: "unknown", flip_y: "asset_defined" }],
    }))).toBe(true);
  });
});

describe("shouldFlipV", () => {
  it("always flips when flip_y is 'true'", () => {
    expect(shouldFlipV(UV_ORIGIN_TOP_LEFT, FLIP_Y_TRUE)).toBe(true);
    expect(shouldFlipV(UV_ORIGIN_BOTTOM_LEFT, FLIP_Y_TRUE)).toBe(true);
  });

  it("never flips when flip_y is 'false'", () => {
    expect(shouldFlipV(UV_ORIGIN_TOP_LEFT, FLIP_Y_FALSE)).toBe(false);
    expect(shouldFlipV(UV_ORIGIN_BOTTOM_LEFT, FLIP_Y_FALSE)).toBe(false);
  });

  it("flips when asset_defined with top_left origin", () => {
    expect(shouldFlipV(UV_ORIGIN_TOP_LEFT, FLIP_Y_ASSET_DEFINED)).toBe(true);
  });

  it("does not flip when asset_defined with bottom_left origin", () => {
    expect(shouldFlipV(UV_ORIGIN_BOTTOM_LEFT, FLIP_Y_ASSET_DEFINED)).toBe(false);
  });

  it("defaults to top-left behavior for unknown origin with asset_defined flip_y", () => {
    expect(shouldFlipV("unknown", FLIP_Y_ASSET_DEFINED)).toBe(true);
  });
});

describe("getNormalizedUvs", () => {
  it("returns null when no uv_sets", () => {
    expect(getNormalizedUvs(makePrimitive())).toBeNull();
  });

  it("returns null when uv_set is empty", () => {
    expect(getNormalizedUvs(makePrimitive({ uv_sets: [] }))).toBeNull();
  });

  it("returns UVs unchanged when origin is bottom_left", () => {
    const uvs = getNormalizedUvs(makePrimitive({
      uv_sets: [{ set_index: 0, uvs: [0, 0, 1, 0.5], origin: "bottom_left", flip_y: "asset_defined" }],
    }));
    expect(uvs).not.toBeNull();
    expect(uvs![0]).toBe(0);
    expect(uvs![1]).toBe(0); // bottom_left: no flip
    expect(uvs![2]).toBe(1);
    expect(uvs![3]).toBe(0.5);
  });

  it("flips V when origin is top_left", () => {
    const uvs = getNormalizedUvs(makePrimitive({
      uv_sets: [{ set_index: 0, uvs: [0, 0, 1, 0.5], origin: "top_left", flip_y: "asset_defined" }],
    }));
    expect(uvs).not.toBeNull();
    expect(uvs![0]).toBe(0);
    expect(uvs![1]).toBe(1); // 1 - 0 = 1
    expect(uvs![2]).toBe(1);
    expect(uvs![3]).toBe(0.5); // 1 - 0.5 = 0.5
  });
});

describe("resolveMaterialProperties", () => {
  it("defaults to double-sided with vertexColors when no material", () => {
    const props = resolveMaterialProperties(null, true, false);
    expect(props.vertexColors).toBe(true);
    expect(props.side).toBe(2); // DoubleSide
    expect(props.transparent).toBe(false);
  });

  it("uses material alpha_mode for transparency", () => {
    const mat = makeMaterial({ alpha_mode: ALPHA_MODE_BLEND });
    const props = resolveMaterialProperties(mat, false, false);
    expect(props.transparent).toBe(true);
    expect(props.depthWrite).toBe(false);
  });

  it("respects double_sided material flag", () => {
    const mat = makeMaterial({ double_sided: true });
    const props = resolveMaterialProperties(mat, false, false);
    expect(props.side).toBe(2); // DoubleSide
  });

  it("sets alphaTest for mask mode", () => {
    const mat = makeMaterial({ alpha_mode: ALPHA_MODE_MASK, alpha_cutoff: 0.3 });
    const props = resolveMaterialProperties(mat, false, false);
    expect(props.transparent).toBe(true);
    expect(props.alphaTest).toBe(0.3);
  });

  it("disables vertexColors when base_color_texture is set", () => {
    const mat = makeMaterial({
      base_color_texture: {
        texture_id: "tex-1",
        uv_set: 0,
        uv_transform: { offset: [0, 0], scale: [1, 1], rotation: 0 },
        uv_origin: "unknown",
        flip_y: "asset_defined",
        wrap_s: "repeat",
        wrap_t: "repeat",
        source_label: "unknown",
      },
    });
    const props = resolveMaterialProperties(mat, true, false);
    expect(props.vertexColors).toBe(false);
  });
});

describe("diagnoseReferencePrimitive", () => {
  it("returns empty for clean primitive without texture", () => {
    const prim = makePrimitive();
    const result = diagnoseReferencePrimitive(prim, null);
    expect(result).toHaveLength(1); // "no vertex colors and no texture" diagnostic
    expect(result[0].severity).toBe("info");
  });

  it("warns when texture assigned but no UVs", () => {
    const mat = makeMaterial({
      base_color_texture: {
        texture_id: "tex-1",
        uv_set: 0,
        uv_transform: { offset: [0, 0], scale: [1, 1], rotation: 0 },
        uv_origin: "unknown",
        flip_y: "asset_defined",
        wrap_s: "repeat",
        wrap_t: "repeat",
        source_label: "unknown",
      },
    });
    const prim = makePrimitive({ uv_sets: [] });
    const diagnostics = diagnoseReferencePrimitive(prim, mat);
    expect(diagnostics.some((d) => d.category === "texture")).toBe(true);
  });
});

describe("computeBoundsFromPositions", () => {
  it("returns null for empty positions", () => {
    expect(computeBoundsFromPositions([])).toBeNull();
  });

  it("computes correct bounds", () => {
    const bounds = computeBoundsFromPositions([-2, -3, -4, 5, 6, 7]);
    expect(bounds!.min_x).toBe(-2);
    expect(bounds!.min_y).toBe(-3);
    expect(bounds!.min_z).toBe(-4);
    expect(bounds!.max_x).toBe(5);
    expect(bounds!.max_y).toBe(6);
    expect(bounds!.max_z).toBe(7);
  });
});
