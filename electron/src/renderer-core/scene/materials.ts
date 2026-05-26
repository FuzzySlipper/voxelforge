/**
 * Three.js material construction helpers for reference model and voxel rendering.
 * Centralizes alpha/sidedness, wireframe toggle, and color-space conventions.
 */

import * as THREE from "three";
import type { RenderMaterial, RenderTextureSlot } from "../protocol/types";
import {
  resolveMaterialProperties,
  type ResolvedMaterialProperties,
} from "./referenceModels";

/**
 * Create a MeshStandardMaterial from reference model data.
 * Handles vertex colors, transparency, alpha mode, double-sidedness,
 * roughness/metalness, and texture slot assignment.
 */
export function createReferenceMaterial(
  renderMaterial: RenderMaterial | null,
  hasVertexColors: boolean,
  vertexAlphaDetected: boolean,
): THREE.MeshStandardMaterial {
  const props = resolveMaterialProperties(
    renderMaterial,
    hasVertexColors,
    vertexAlphaDetected,
  );

  return new THREE.MeshStandardMaterial({
    vertexColors: props.vertexColors,
    side: props.side as THREE.Side,
    roughness: props.roughness,
    metalness: props.metalness,
    transparent: props.transparent,
    opacity: props.opacity,
    depthWrite: props.depthWrite,
    alphaTest: props.alphaTest,
  });
}

/**
 * Create a voxel (cube) material with vertex colors.
 */
export function createVoxelMaterial(
  hasVertexColors: boolean,
  isWireframe = false,
): THREE.MeshStandardMaterial {
  return new THREE.MeshStandardMaterial({
    vertexColors: hasVertexColors,
    side: THREE.FrontSide,
    roughness: 0.7,
    metalness: 0.1,
    wireframe: isWireframe,
  });
}

/**
 * Apply texture to an existing material.
 * Handles color-space conversion (srgb vs linear).
 */
export function applyTextureToMaterial(
  material: THREE.MeshStandardMaterial,
  texture: THREE.Texture,
  textureSlot?: RenderTextureSlot | null,
): void {
  const colorSpace = textureSlot
    ? inferColorSpace(textureSlot)
    : "srgb";

  if (colorSpace === "linear") {
    texture.colorSpace = THREE.LinearSRGBColorSpace;
  } else {
    texture.colorSpace = THREE.SRGBColorSpace;
  }

  material.map = texture;
  material.needsUpdate = true;
}

/**
 * Infer the Three.js color space from a texture slot's color_space field.
 */
export function inferColorSpace(textureSlot: RenderTextureSlot): "srgb" | "linear" {
  if (textureSlot.source_label?.includes("normal")) return "linear";
  if (textureSlot.source_label?.includes("roughness")) return "linear";
  if (textureSlot.source_label?.includes("metallic")) return "linear";
  return "srgb";
}

/**
 * Apply wireframe to all materials in a list of meshes.
 */
export function applyWireframe(
  objects: THREE.Object3D[],
  visible: boolean,
): void {
  for (const obj of objects) {
    if (obj instanceof THREE.Mesh) {
      const mat = obj.material;
      if (mat instanceof THREE.MeshStandardMaterial) {
        mat.wireframe = visible;
      }
    }
    if (obj.children.length > 0) {
      applyWireframe(obj.children, visible);
    }
  }
}

/**
 * Dispose of a material and its texture map.
 */
export function disposeMaterial(material: THREE.Material): void {
  if (material instanceof THREE.MeshStandardMaterial) {
    if (material.map) {
      material.map.dispose();
    }
    if (material.emissiveMap) {
      material.emissiveMap.dispose();
    }
  }
  material.dispose();
}
