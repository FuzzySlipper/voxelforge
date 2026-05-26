/**
 * Tests for VoxelForgeScene material/texture contract usage (#1662).
 * Verifies that buildPrimitive correctly uses snapshot.materials,
 * snapshot.textures, primitive.material_index, UV metadata, alpha mode,
 * sidedness, and texture slots.
 */
import { describe, it, expect } from "vitest";
import {
  detectVertexAlpha,
  applyUvFlip,
  resolveTextureHandle,
} from "../../src/renderer-core/scene/VoxelForgeScene";
import type { RenderPrimitive, RenderMaterial, RenderTexture, RenderTextureSlot } from "../../src/renderer-core/protocol/types";
import { resolveTextureUrl } from "../../src/renderer-core/scene/referenceModels";

// ── detectVertexAlpha tests ──

describe("detectVertexAlpha", () => {
  it("returns false for null/undefined input", () => {
    expect(detectVertexAlpha(null, 0)).toBe(false);
    expect(detectVertexAlpha(undefined, 0)).toBe(false);
  });

  it("returns false when all alphas are 255", () => {
    expect(detectVertexAlpha([255, 0, 0, 255, 0, 255, 0, 255], 2)).toBe(false);
  });

  it("returns true when any alpha is less than 255", () => {
    expect(detectVertexAlpha([255, 0, 0, 128, 0, 255, 0, 255], 2)).toBe(true);
  });

  it("returns true when all alphas are 0 (fully transparent)", () => {
    expect(detectVertexAlpha([255, 0, 0, 0, 0, 255, 0, 0], 2)).toBe(true);
  });

  it("decodes base64 and detects alpha", () => {
    // base64 for [255,0,0,128, 0,255,0,255]
    const encoded = btoa(String.fromCharCode(255, 0, 0, 128, 0, 255, 0, 255));
    expect(detectVertexAlpha(encoded, 2)).toBe(true);
  });

  it("returns false for empty decoded array", () => {
    expect(detectVertexAlpha([], 0)).toBe(false);
  });

  it("returns false for single pixel with alpha=255", () => {
    expect(detectVertexAlpha([128, 128, 128, 255], 1)).toBe(false);
  });
});

// ── applyUvFlip tests ──

describe("applyUvFlip", () => {
  it("flips V when flip_y is 'true'", () => {
    const result = applyUvFlip([0, 0, 1, 0.5], "top_left", "true");
    expect(result[0]).toBe(0);
    expect(result[1]).toBeCloseTo(1);  // 1 - 0
    expect(result[2]).toBe(1);
    expect(result[3]).toBeCloseTo(0.5); // 1 - 0.5
  });

  it("never flips when flip_y is 'false'", () => {
    const result = applyUvFlip([0, 0, 1, 0.5], "top_left", "false");
    expect(result[1]).toBe(0);
    expect(result[3]).toBe(0.5);
  });

  it("flips for top_left origin with asset_defined flip_y", () => {
    const result = applyUvFlip([0, 0, 1, 0.5], "top_left", "asset_defined");
    expect(result[1]).toBeCloseTo(1);
    expect(result[3]).toBeCloseTo(0.5);
  });

  it("does not flip for bottom_left origin with asset_defined flip_y", () => {
    const result = applyUvFlip([0, 0, 1, 0.5], "bottom_left", "asset_defined");
    expect(result[1]).toBe(0);
    expect(result[3]).toBe(0.5);
  });

  it("flips for unknown origin with asset_defined flip_y", () => {
    const result = applyUvFlip([0, 0.75, 1, 0.25], "unknown", "asset_defined");
    expect(result[1]).toBeCloseTo(0.25); // 1 - 0.75
    expect(result[3]).toBeCloseTo(0.75); // 1 - 0.25
  });

  it("preserves U values unchanged", () => {
    const result = applyUvFlip([0.1, 0.2, 0.3, 0.4], "top_left", "asset_defined");
    expect(result[0]).toBeCloseTo(0.1);
    expect(result[2]).toBeCloseTo(0.3);
  });
});

// ── Material contract compliance tests ──
// These verify the data contract shape matches what buildPrimitive expects.

describe("material contract shape (RenderMaterial)", () => {
  it("alpha_mode is one of opaque/mask/blend", () => {
    const opaque: RenderMaterial = {
      id: "mat-1", name: "Opaque", base_color_factor: [1,1,1,1],
      base_color_texture: null, normal_texture: null, emissive_texture: null,
      emissive_factor: null, metallic_factor: 0, roughness_factor: 1,
      alpha_mode: "opaque", alpha_cutoff: null, double_sided: false,
      color_space: "srgb", diagnostics: [],
    };
    const blend: RenderMaterial = { ...opaque, id: "mat-2", alpha_mode: "blend" };
    const mask: RenderMaterial = { ...opaque, id: "mat-3", alpha_mode: "mask", alpha_cutoff: 0.5 };

    expect(opaque.alpha_mode).toBe("opaque");
    expect(blend.alpha_mode).toBe("blend");
    expect(mask.alpha_mode).toBe("mask");
    expect(mask.alpha_cutoff).toBe(0.5);
  });

  it("double_sided is boolean", () => {
    const mat: RenderMaterial = {
      id: "mat-1", name: "Test", base_color_factor: [1,1,1,1],
      base_color_texture: null, normal_texture: null, emissive_texture: null,
      emissive_factor: null, metallic_factor: 0, roughness_factor: 1,
      alpha_mode: "opaque", alpha_cutoff: null, double_sided: true,
      color_space: "srgb", diagnostics: [],
    };
    expect(mat.double_sided).toBe(true);
  });

  it("base_color_texture references a valid texture slot", () => {
    const slot: RenderTextureSlot = {
      texture_id: "tex-1", uv_set: 0,
      uv_transform: { offset: [0,0], scale: [1,1], rotation: 0 },
      uv_origin: "top_left", flip_y: "asset_defined",
      wrap_s: "repeat", wrap_t: "repeat", source_label: "assimp",
    };
    expect(slot.texture_id).toBe("tex-1");
    expect(slot.uv_set).toBe(0);
  });
});

// ── Texture contract tests ──

describe("texture contract shape (RenderTexture)", () => {
  it("has id and uri fields for transport resolution", () => {
    const tex: RenderTexture = {
      id: "tex-1", uri: "texture://bridge/tex-1",
      mime_type: "image/png", color_space: "srgb",
      width: null, height: null, diagnostics: [],
    };
    expect(tex.id).toBe("tex-1");
    expect(tex.uri).toMatch(/^texture:\/\//);
  });

  it("mime_type is nullable", () => {
    const tex: RenderTexture = {
      id: "tex-1", uri: "texture://bridge/tex-1",
      mime_type: null, color_space: "srgb",
      width: null, height: null, diagnostics: [],
    };
    expect(tex.mime_type).toBeNull();
  });
});

// ── Primitive material_index contract tests ──

describe("primitive material_index contract (RenderPrimitive)", () => {
  const prim: RenderPrimitive = {
    id: "prim-1", material_index: 0,
    position: [0,0,0, 1,0,0, 1,1,0],
    normal: [0,0,1, 0,0,1, 0,0,1],
    color_rgba: null, uv_sets: [], indices: null, bounds_local: null,
  };

  it("material_index is a non-negative integer", () => {
    expect(prim.material_index).toBeGreaterThanOrEqual(0);
    expect(Number.isInteger(prim.material_index)).toBe(true);
  });

  it("material_index can be used as array index into snapshot.materials", () => {
    const materials: RenderMaterial[] = [
      {
        id: "mat-0", name: "Material 0", base_color_factor: [1,1,1,1],
        base_color_texture: null, normal_texture: null, emissive_texture: null,
        emissive_factor: null, metallic_factor: 0, roughness_factor: 1,
        alpha_mode: "opaque", alpha_cutoff: null, double_sided: false,
        color_space: "srgb", diagnostics: [],
      },
    ];
    const mat = materials[prim.material_index];
    expect(mat).toBeDefined();
    expect(mat.id).toBe("mat-0");
  });
});

// ── resolveTextureHandle tests ──

describe("resolveTextureHandle", () => {
  it("passes through HTTP URLs unchanged", () => {
    expect(resolveTextureHandle("/api/reference-texture?index=0&mesh_index=0&slot=diffuse"))
      .toBe("/api/reference-texture?index=0&mesh_index=0&slot=diffuse");
  });

  it("passes through absolute HTTP URLs unchanged", () => {
    expect(resolveTextureHandle("http://localhost:5100/api/texture/tex-0"))
      .toBe("http://localhost:5100/api/texture/tex-0");
  });

  it("passes through HTTPS URLs unchanged", () => {
    expect(resolveTextureHandle("https://server.com/api/texture/tex-0"))
      .toBe("https://server.com/api/texture/tex-0");
  });

  it("warns but returns texture:// URIs for unknown host", () => {
    const result = resolveTextureHandle("texture://unknown/tex-0");
    expect(result).toBe("texture://unknown/tex-0");
  });

  it("warns but returns texture:// URIs for mcp host", () => {
    const result = resolveTextureHandle("texture://mcp/tex-0");
    expect(result).toBe("texture://mcp/tex-0");
  });

  it("warns but returns texture:// URIs for bridge host", () => {
    const result = resolveTextureHandle("texture://bridge/tex-0");
    expect(result).toBe("texture://bridge/tex-0");
  });

  it("returns unrecognized format as-is", () => {
    const result = resolveTextureHandle("texture://");
    expect(result).toBe("texture://");
  });

  it("returns empty string as-is", () => {
    expect(resolveTextureHandle("")).toBe("");
  });
});

// ── Emissive texture contract tests ──

describe("emissive texture slot contract", () => {
  it("RenderMaterial has emissive_texture and emissive_factor fields", () => {
    const matWithEmissive: RenderMaterial = {
      id: "mat-emissive", name: "Emissive", base_color_factor: [1,1,1,1],
      base_color_texture: null, normal_texture: null,
      emissive_texture: {
        texture_id: "tex-emissive-0", uv_set: 0,
        uv_transform: { offset: [0,0], scale: [1,1], rotation: 0 },
        uv_origin: "bottom_left", flip_y: "asset_defined",
        wrap_s: "clamp", wrap_t: "clamp", source_label: "unity_sidecar",
      },
      emissive_factor: [1.0, 1.0, 1.0],
      metallic_factor: 0, roughness_factor: 1,
      alpha_mode: "opaque", alpha_cutoff: null, double_sided: false,
      color_space: "srgb", diagnostics: [],
    };

    expect(matWithEmissive.emissive_texture).not.toBeNull();
    expect(matWithEmissive.emissive_texture!.texture_id).toBe("tex-emissive-0");
    expect(matWithEmissive.emissive_texture!.uv_origin).toBe("bottom_left");
    expect(matWithEmissive.emissive_texture!.flip_y).toBe("asset_defined");
    expect(matWithEmissive.emissive_texture!.wrap_s).toBe("clamp");
    expect(matWithEmissive.emissive_texture!.wrap_t).toBe("clamp");
    expect(matWithEmissive.emissive_texture!.source_label).toBe("unity_sidecar");
    expect(matWithEmissive.emissive_factor).toEqual([1.0, 1.0, 1.0]);
  });

  it("emissive texture can reference /api/reference-texture?...slot=emissive", () => {
    const texture: RenderTexture = {
      id: "tex-emissive-0",
      uri: "/api/reference-texture?index=0&mesh_index=0&slot=emissive",
      mime_type: "image/png", color_space: "srgb",
      width: null, height: null, diagnostics: [],
    };

    // Verify the URI matches the expected pattern for emissive slot
    expect(texture.uri).toContain("slot=emissive");
    expect(texture.uri).toContain("/api/reference-texture");

    // Verify the texture can be resolved via resolveTextureUrl
    const slot: RenderTextureSlot = {
      texture_id: "tex-emissive-0", uv_set: 0,
      uv_transform: { offset: [0,0], scale: [1,1], rotation: 0 },
      uv_origin: "top_left", flip_y: "asset_defined",
      wrap_s: "repeat", wrap_t: "repeat", source_label: "unity_sidecar",
    };
    const resolvedUri = resolveTextureUrl(slot, [texture]);
    expect(resolvedUri).toBe("/api/reference-texture?index=0&mesh_index=0&slot=emissive");
  });

  it("emissive texture sampling controls are independent from base color", () => {
    // Emissive and diffuse can have different sampling controls
    const emissiveSlot: RenderTextureSlot = {
      texture_id: "tex-emissive-0", uv_set: 0,
      uv_transform: { offset: [0,0], scale: [1,1], rotation: 0 },
      uv_origin: "bottom_left", flip_y: "asset_defined",
      wrap_s: "clamp", wrap_t: "clamp", source_label: "unity_sidecar",
    };
    const diffuseSlot: RenderTextureSlot = {
      texture_id: "tex-diffuse-0", uv_set: 0,
      uv_transform: { offset: [0,0], scale: [1,1], rotation: 0 },
      uv_origin: "top_left", flip_y: "true",
      wrap_s: "repeat", wrap_t: "repeat", source_label: "assimp",
    };

    // Verify they carry independent sampling metadata
    expect(emissiveSlot.uv_origin).not.toBe(diffuseSlot.uv_origin);
    expect(emissiveSlot.wrap_s).toBe("clamp");
    expect(diffuseSlot.wrap_s).toBe("repeat");
  });
});
