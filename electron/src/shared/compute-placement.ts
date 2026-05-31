/**
 * Shape of a voxel hit from raycasting — pure data used for placement computation.
 */
export interface VoxelRaycastHit {
  /** The voxel position that was hit. */
  position: { x: number; y: number; z: number };
  /** Exact world/render-space point on the intersected face. */
  world_position?: { x: number; y: number; z: number };
  /** The face normal of the hit (which face was intersected). */
  normal: { x: number; y: number; z: number };
  /** Palette index of the hit voxel, or 0 for air. */
  palette_index: number;
  /** Screen-space coordinates in pixels. */
  screen: { x: number; y: number };
  /** World-space ray origin. */
  ray_origin: { x: number; y: number; z: number };
  /** World-space ray direction. */
  ray_direction?: { x: number; y: number; z: number };
  /** Distance along the ray. */
  distance: number;
  /** Optional renderer object metadata for debug overlays. */
  hit_object_type?: string;
  hit_object_id?: string;
}

/**
 * Compute the placement position for a new voxel adjacent to a hit voxel.
 * This is a pure computation: no editor state mutation here.
 * The result is the integer coordinate of the cell directly adjacent
 * to the hit voxel along the face normal.
 */
export function computePlacementPosition(
  hit: VoxelRaycastHit,
): { x: number; y: number; z: number } {
  return {
    x: hit.position.x + hit.normal.x,
    y: hit.position.y + hit.normal.y,
    z: hit.position.z + hit.normal.z,
  };
}
