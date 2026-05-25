/**
 * Reference model rendering utilities.
 * Centralizes UV/material/texture interpretation for reference models
 * including alpha/sidedness, color-space, flip-y conventions, and diagnostics.
 */

import type { RenderPrimitive, RenderTextureSlot, RenderMaterial, BoundsDto } from "../protocol/types";

// ── Constants ──

export const UV_ORIGIN_TOP_LEFT = "top_left";
export const UV_ORIGIN_BOTTOM_LEFT = "bottom_left";
export const UV_ORIGIN_ASSET_DEFINED = "asset_defined";
export const UV_ORIGIN_UNKNOWN = "unknown";

export const FLIP_Y_ASSET_DEFINED = "asset_defined";
export const FLIP_Y_TRUE = "true";
export const FLIP_Y_FALSE = "false";

export const ALPHA_MODE_OPAQUE = "opaque";
export const ALPHA_MODE_MASK = "mask";
export const ALPHA_MODE_BLEND = "blend";

export const COLOR_SPACE_SRGB = "srgb";
export const COLOR_SPACE_LINEAR = "linear";

// ── Texture readiness ──

export interface TextureLoadState {
  textureId: string;
  uri: string;
  loading: boolean;
  loaded: boolean;
  error: string | null;
}

/**
 * Check whether a primitive has UV coordinates available.
 */
export function hasUvs(prim: RenderPrimitive): boolean {
  return (
    prim.uv_sets != null &&
    prim.uv_sets.length > 0 &&
    prim.uv_sets[0].uvs != null &&
    prim.uv_sets[0].uvs.length > 0
  );
}

/**
 * Determine whether the V axis needs flipping based on UV origin.
 * OpenGL convention: bottom-left origin, V up (no flip).
 * DirectX/HTML Canvas: top-left origin, V down (flip Y).
 * Three.js default: top-left origin (like HTML Canvas).
 */
export function shouldFlipV(uvOrigin: string, flipY: string): boolean {
  if (flipY === FLIP_Y_TRUE) return true;
  if (flipY === FLIP_Y_FALSE) return false;
  if (flipY === FLIP_Y_ASSET_DEFINED || flipY === "unknown") {
    // When asset-defined or unknown, infer from origin.
    // Default to top-left (Three.js/HTML Canvas convention) for unknown origins.
    return uvOrigin !== UV_ORIGIN_BOTTOM_LEFT;
  }
  // Default: flip for top-left origin (Three.js convention)
  return uvOrigin !== UV_ORIGIN_BOTTOM_LEFT;
}

/**
 * Get the UV data for a primitive, accounting for flip_y and uv_origin.
 * Returns [u, v, u, v, ...] with V flipped if needed.
 */
export function getNormalizedUvs(
  prim: RenderPrimitive,
  uvSetIndex = 0,
): Float32Array | null {
  if (!prim.uv_sets || uvSetIndex >= prim.uv_sets.length) return null;

  const uvSet = prim.uv_sets[uvSetIndex];
  if (!uvSet.uvs || uvSet.uvs.length === 0) return null;

  const uvs = new Float32Array(uvSet.uvs);
  const flipV = shouldFlipV(uvSet.origin, uvSet.flip_y);

  if (flipV) {
    // Flip V: v = 1 - v
    for (let i = 1; i < uvs.length; i += 2) {
      uvs[i] = 1 - uvs[i];
    }
  }

  return uvs;
}

// ── Material interpretation ──

export interface ResolvedMaterialProperties {
  vertexColors: boolean;
  side: number; // THREE.Side enum value
  roughness: number;
  metalness: number;
  transparent: boolean;
  opacity: number;
  depthWrite: boolean;
  alphaTest?: number;
}

/**
 * Resolve material properties from a RenderMaterial (when available)
 * with sensible defaults for reference model rendering.
 *
 * @param material - RenderMaterial from the snapshot, or null for defaults
 * @param hasVertexColors - Whether the primitive has vertex colors
 * @param vertexAlphaDetected - Whether vertex colors have non-255 alpha
 */
export function resolveMaterialProperties(
  material: RenderMaterial | null,
  hasVertexColors: boolean,
  vertexAlphaDetected: boolean,
): ResolvedMaterialProperties {
  if (!material) {
    return {
      vertexColors: hasVertexColors,
      side: 2, // THREE.DoubleSide = 2
      roughness: 0.6,
      metalness: 0.05,
      transparent: vertexAlphaDetected,
      opacity: 1.0,
      depthWrite: !vertexAlphaDetected,
    };
  }

  const isTransparent =
    material.alpha_mode === ALPHA_MODE_BLEND ||
    material.alpha_mode === ALPHA_MODE_MASK ||
    vertexAlphaDetected;

  return {
    vertexColors: hasVertexColors && !material.base_color_texture,
    side: material.double_sided ? 2 : 0, // DoubleSide or FrontSide
    roughness: material.roughness_factor ?? 0.6,
    metalness: material.metallic_factor ?? 0.05,
    transparent: isTransparent,
    opacity: material.base_color_factor?.[3] ?? 1.0,
    depthWrite: material.alpha_mode === ALPHA_MODE_OPAQUE,
    alphaTest: material.alpha_mode === ALPHA_MODE_MASK
      ? material.alpha_cutoff ?? 0.5
      : undefined,
  };
}

/**
 * Resolve the texture URL for a primitive's material slot, if applicable.
 * Returns null if no texture is assigned or if UVs are missing.
 */
export function resolveTextureUrl(
  textureSlot: RenderTextureSlot | null | undefined,
  textures: Array<{ id: string; uri: string }>,
): string | null {
  if (!textureSlot) return null;
  const tex = textures.find((t) => t.id === textureSlot.texture_id);
  return tex?.uri ?? null;
}

// ── Diagnostics ──

export interface ReferenceDiagnostic {
  severity: "info" | "warning" | "error";
  category: string;
  message: string;
}

/**
 * Check for common reference-model rendering issues and return diagnostics.
 */
export function diagnoseReferencePrimitive(
  prim: RenderPrimitive,
  material: RenderMaterial | null,
): ReferenceDiagnostic[] {
  const diagnostics: ReferenceDiagnostic[] = [];

  // Check for texture without UVs
  const hasTexSlot = material?.base_color_texture != null;
  const hasUvData = hasUvs(prim);

  if (hasTexSlot && !hasUvData) {
    diagnostics.push({
      severity: "warning",
      category: "texture",
      message:
        `Primitive ${prim.id} has a base color texture assigned but no UV coordinates. ` +
        "Texture will not render; using vertex colors as fallback.",
    });
  }

  // Check for missing vertex colors
  if (!prim.color_rgba && !hasTexSlot) {
    diagnostics.push({
      severity: "info",
      category: "material",
      message:
        `Primitive ${prim.id} has no vertex colors and no texture — rendering as solid gray.`,
    });
  }

  // Check for large vertex counts
  const vertexCount = prim.position.length / 3;
  if (vertexCount > 100000) {
    diagnostics.push({
      severity: "warning",
      category: "performance",
      message:
        `Primitive ${prim.id} has ${vertexCount} vertices — may impact rendering performance.`,
    });
  }

  return diagnostics;
}

// ── Bounds helpers ──

/**
 * Compute the bounding box from a flat position array [x0,y0,z0, ...].
 */
export function computeBoundsFromPositions(
  positions: number[],
): BoundsDto | null {
  if (positions.length < 3) return null;

  let minX = Infinity, minY = Infinity, minZ = Infinity;
  let maxX = -Infinity, maxY = -Infinity, maxZ = -Infinity;

  for (let i = 0; i < positions.length; i += 3) {
    const x = positions[i];
    const y = positions[i + 1];
    const z = positions[i + 2];
    if (x < minX) minX = x;
    if (y < minY) minY = y;
    if (z < minZ) minZ = z;
    if (x > maxX) maxX = x;
    if (y > maxY) maxY = y;
    if (z > maxZ) maxZ = z;
  }

  return {
    min_x: minX,
    min_y: minY,
    min_z: minZ,
    max_x: maxX,
    max_y: maxY,
    max_z: maxZ,
  };
}
