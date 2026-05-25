/**
 * TypeScript types matching the #1657 RenderSceneSnapshot snake_case JSON contract.
 * These correspond 1:1 to C# types in VoxelForge.App.Render.* and serve as
 * the canonical TS contract for all render-client implementations.
 *
 * Schema version: "voxelforge.render_scene@1"
 */

// ── Top-level snapshot ──

export interface RenderSceneSnapshot {
  schema_version: string;
  revision: number;
  model_id: string;
  source: RenderSourceInfo;
  bounds: BoundsDto | null;
  reference_bounds: BoundsDto | null;
  combined_bounds: BoundsDto | null;
  voxel_meshes: RenderVoxelMesh[];
  reference_nodes: RenderReferenceNode[];
  materials: RenderMaterial[];
  textures: RenderTexture[];
  palette: RenderPaletteEntry[];
  diagnostics: RenderDiagnostic[];
}

export interface RenderSourceInfo {
  host: string;
  capabilities: string[];
}

// ── Voxel mesh ──

export interface RenderVoxelMesh {
  id: string;
  revision: number;
  /** Flat [x0,y0,z0, x1,y1,z1, ...] */
  positions: number[];
  /** Flat [nx0,ny0,nz0, ...] */
  normals: number[];
  /** RGBA bytes — may be number[] or base64 string from C# */
  colors_rgba: number[] | string;
  /** Per-vertex palette indices — may be number[] or base64 string */
  palette_indices: number[] | string;
  /** Triangle index buffer */
  indices: number[];
  bounds: BoundsDto | null;
  payload_format: string;
}

// ── Reference nodes and primitives ──

export interface RenderReferenceNode {
  id: string;
  display_name: string;
  source_format: string;
  source_asset_id: string | null;
  visible: boolean;
  render_mode: string;
  transform: RenderTransform;
  bounds_local: BoundsDto | null;
  bounds_world: BoundsDto | null;
  primitives: RenderPrimitive[];
  diagnostics: RenderDiagnostic[];
}

export interface RenderPrimitive {
  id: string;
  material_index: number;
  /** Flat [x0,y0,z0, ...] */
  position: number[];
  /** Flat [nx0,ny0,nz0, ...] */
  normal: number[];
  /** RGBA bytes or null */
  color_rgba: number[] | string | null;
  /** UV sets for texture mapping */
  uv_sets: RenderUvSet[];
  /** Triangle index buffer or null (non-indexed) */
  indices: number[] | null;
  bounds_local: BoundsDto | null;
}

export interface RenderUvSet {
  set_index: number;
  /** Flat [u0,v0, u1,v1, ...] */
  uvs: number[];
  /** "top_left", "bottom_left", "asset_defined", "unknown" */
  origin: string;
  /** Whether V is flipped: boolean string or "asset_defined" */
  flip_y: string;
}

// ── Transform ──

export interface RenderTransform {
  position_x: number;
  position_y: number;
  position_z: number;
  rotation_x: number;
  rotation_y: number;
  rotation_z: number;
  scale: number;
}

// ── Materials ──

export interface RenderMaterial {
  id: string;
  name: string;
  /** RGBA [r,g,b,a] in 0..1 range */
  base_color_factor: number[];
  base_color_texture: RenderTextureSlot | null;
  normal_texture: RenderTextureSlot | null;
  emissive_texture: RenderTextureSlot | null;
  emissive_factor: number[] | null;
  metallic_factor: number;
  roughness_factor: number;
  /** "opaque", "mask", "blend" */
  alpha_mode: string;
  alpha_cutoff: number | null;
  double_sided: boolean;
  /** "srgb", "linear", "unknown" */
  color_space: string;
  diagnostics: RenderDiagnostic[];
}

export interface RenderTextureSlot {
  texture_id: string;
  uv_set: number;
  uv_transform: RenderUvTransform;
  /** "top_left", "bottom_left", "asset_defined", "unknown" */
  uv_origin: string;
  /** boolean string or "asset_defined" */
  flip_y: string;
  /** "clamp", "repeat", "mirror", "unknown" */
  wrap_s: string;
  /** "clamp", "repeat", "mirror", "unknown" */
  wrap_t: string;
  /** "assimp", "unity_sidecar", "manual_override", "generated", "unknown" */
  source_label: string;
}

export interface RenderUvTransform {
  /** UV offset: [u, v] */
  offset: number[];
  /** UV scale: [u, v] */
  scale: number[];
  /** Rotation in radians */
  rotation: number;
}

// ── Textures ──

export interface RenderTexture {
  id: string;
  /** Session-authorized URI or transport handle */
  uri: string;
  mime_type: string | null;
  /** "srgb", "linear", "unknown" */
  color_space: string;
  width: number | null;
  height: number | null;
  diagnostics: RenderDiagnostic[];
}

// ── Palette entries ──

export interface RenderPaletteEntry {
  index: number;
  name: string;
  r: number;
  g: number;
  b: number;
  a: number;
  visible: boolean;
}

// ── Shared DTOs ──

export interface BoundsDto {
  min_x: number;
  min_y: number;
  min_z: number;
  max_x: number;
  max_y: number;
  max_z: number;
}

export interface RenderDiagnostic {
  severity: string;
  category: string;
  message: string;
}

// ── Transitional types (backward compat with pre-#1657 viewer) ──

/** @deprecated Use RenderSceneSnapshot. Kept for pre-#1657 viewer compatibility. */
export interface TransitionalViewerState {
  model_name: string;
  voxel_count: number;
  revision: number;
  grid_hint: number;
  reference_model_count: number;
  reference_vertex_count: number;
  palette_entries: Array<{
    index: number;
    name: string;
    color: string;
    a: number;
    visible: boolean;
  }>;
  bounds: {
    min_x: number;
    min_y: number;
    min_z: number;
    max_x: number;
    max_y: number;
    max_z: number;
  } | null;
}

/** @deprecated Use RenderSceneSnapshot. Kept for pre-#1657 viewer compatibility. */
export interface TransitionalMeshSnapshot {
  model_id: string;
  mesh_id: string;
  format: string;
  vertex_count: number;
  index_count: number;
  triangle_count: number;
  positions: number[];
  normals: number[];
  colors: number[] | string;
  indices: number[];
  bounds: {
    min_x: number; min_y: number; min_z: number;
    max_x: number; max_y: number; max_z: number;
  } | null;
  combined_bounds?: {
    min_x: number; min_y: number; min_z: number;
    max_x: number; max_y: number; max_z: number;
  } | null;
  palette_mapping?: Record<string, { name: string; color: string; a: number; visible: boolean }> | null;
  metrics?: {
    mesh_generation_ms: number;
    serialization_ms: number;
    total_ms: number;
  } | null;
  reference_models?: Array<{
    file_name: string;
    format: string;
    is_visible: boolean;
    position_x: number; position_y: number; position_z: number;
    rotation_x: number; rotation_y: number; rotation_z: number;
    scale: number;
    index: number;
    total_vertices: number;
    total_triangles: number;
    positions: number[];
    normals: number[];
    colors: number[];
    uvs: number[];
    indices: number[];
    bounds: { min_x: number; min_y: number; min_z: number; max_x: number; max_y: number; max_z: number } | null;
    meshes?: Array<{
      mesh_index: number;
      material_name: string;
      vertex_count: number;
      triangle_count: number;
      positions: number[];
      normals: number[];
      colors: number[];
      uvs: number[];
      has_uvs: boolean;
      indices: number[];
      diffuse_texture_path: string | null;
      normal_texture_path: string | null;
      emissive_texture_path: string | null;
      diffuse_source_label?: string;
    }>;
  }> | null;
}

// ── Events ──

export interface SseRevisionEvent {
  type: "revision";
  revision: number;
}

export interface SseConnectedEvent {
  type: "connected";
  revision: number;
}

export type ViewerSseEvent =
  | SseRevisionEvent
  | SseConnectedEvent;

// ── Render commands ──

export interface RenderCommandFrameCamera {
  command: "frame_camera";
  params: Record<string, never>;
}

export interface RenderCommandSetGridVisible {
  command: "set_grid_visible";
  params: { visible: boolean };
}

export interface RenderCommandSetWireframe {
  command: "set_wireframe";
  params: { visible: boolean };
}

export interface RenderCommandSetBackgroundColor {
  command: "set_background_color";
  params: { r: number; g: number; b: number };
}

export interface RenderCommandCaptureScreenshot {
  command: "capture_screenshot";
  params: { width?: number; height?: number; format?: "png" | "jpeg" };
}

export type RenderCommand =
  | RenderCommandFrameCamera
  | RenderCommandSetGridVisible
  | RenderCommandSetWireframe
  | RenderCommandSetBackgroundColor
  | RenderCommandCaptureScreenshot;

/**
 * Snapshot of render state for diagnostics and capture readiness.
 */
export interface RenderStateSummary {
  connected: boolean;
  model_name: string;
  voxel_count: number;
  revision: number;
  reference_model_count: number;
  reference_vertex_count: number;
  mesh_build_info?: {
    mesh_generation_ms: number;
    serialization_ms: number;
    total_ms: number;
  } | null;
  scene_metrics?: {
    scene_construction_ms: number;
    first_render_ms: number;
    vertex_count: number;
    triangle_count: number;
    webgl_fallback: boolean;
  } | null;
  pending_texture_loads: number;
  capture_ready: boolean;
}
