/**
 * VoxelForgeScene — core Three.js scene/camera/renderer management.
 *
 * Extracted from electron/src/renderer/scene.ts into renderer-core.
 * Owns the scene, camera, lights, grid, and mesh rendering.
 * Supports both WebGL and fallback (no-GPU) mode.
 *
 * Compatible with the #1657 RenderSceneSnapshot contract via protocol/normalizeSnapshot.
 */

import * as THREE from "three";
import { OrbitControls } from "three/examples/jsm/controls/OrbitControls.js";
import type { BoundsDto, RenderSceneSnapshot, RenderVoxelMesh, RenderPrimitive } from "../protocol/types";
import { decodeByteArray } from "../../shared/byte-utils";
import { VoxelRaycastHit, computePlacementPosition } from "../../shared/compute-placement";

// Re-export for consumers
export type { VoxelRaycastHit };

// ── Render metrics ──

export interface RendererMetrics {
  scene_construction_ms: number;
  first_render_ms: number;
  vertex_count: number;
  triangle_count: number;
  mesh_transfer_ms: number;
  total_renderer_ms: number;
  webgl_fallback?: boolean;
}

// ── Scene options ──

export interface VoxelForgeSceneOptions {
  /** Background color hex (default: 0x2b2b2b) */
  background?: number;
  /** Grid helper subdivisions (default: 100) */
  gridSize?: number;
  /** Grid helper divisions (default: 100) */
  gridDivisions?: number;
  /** Grid color (default: 0x555555) */
  gridColorCenter?: number;
  /** Grid color (default: 0x333333) */
  gridColorOther?: number;
}

// ── Pure decision function ──

/**
 * Pure decision function: should the camera be framed?
 *
 * @param initialCameraFramed - Whether the camera has been framed at least once for the current model.
 * @param isExplicitFrameRequest - Whether this is a user-initiated frame request.
 * @returns true if the camera should be framed, false to preserve current orbit/pan/zoom.
 */
export function shouldFrameCamera(
  initialCameraFramed: boolean,
  isExplicitFrameRequest: boolean,
): boolean {
  if (isExplicitFrameRequest) return true;
  return !initialCameraFramed;
}

// ── Scene class ──

export class VoxelForgeScene {
  protected scene: THREE.Scene;
  protected camera: THREE.PerspectiveCamera;
  protected renderer: THREE.WebGLRenderer | null = null;
  protected controls: OrbitControls | null = null;
  protected meshGroup: THREE.Group;
  protected referenceGroup: THREE.Group;
  protected gridHelper: THREE.GridHelper | null = null;
  protected lastBounds: BoundsDto | null = null;
  protected animationFrameId: number | null = null;
  protected lastModelId: string | null = null;
  protected initialCameraFramed = false;
  private renderCallbacks: ((metrics: RendererMetrics) => void)[] = [];
  private container: HTMLElement;
  protected webglAvailable: boolean;

  constructor(container: HTMLElement, options: VoxelForgeSceneOptions = {}) {
    this.container = container;
    this.webglAvailable = true;

    // Scene
    this.scene = new THREE.Scene();
    this.scene.background = new THREE.Color(options.background ?? 0x2b2b2b);

    // Camera
    this.camera = new THREE.PerspectiveCamera(
      45,
      container.clientWidth / container.clientHeight,
      0.1,
      10000,
    );

    // Try to create WebGL renderer; fall back gracefully if unavailable
    try {
      this.renderer = new THREE.WebGLRenderer({ antialias: true });
      this.renderer.setSize(container.clientWidth, container.clientHeight);
      this.renderer.setPixelRatio(window.devicePixelRatio);
      this.renderer.shadowMap.enabled = true;
      this.renderer.shadowMap.type = THREE.PCFSoftShadowMap;
      container.appendChild(this.renderer.domElement);

      // Orbit controls
      this.controls = new OrbitControls(this.camera, this.renderer.domElement);
      this.controls.enableDamping = true;
      this.controls.dampingFactor = 0.1;
      this.controls.screenSpacePanning = true;
      this.controls.minDistance = 1;
      this.controls.maxDistance = 500;
      this.controls.maxPolarAngle = Math.PI;

      // Start render loop
      this.animate();
    } catch (err) {
      console.warn("[VoxelForgeScene] WebGL not available; fallback mode:", err);
      this.webglAvailable = false;
      container.innerHTML = `<div style="color: #aaa; padding: 20px; font-family: monospace;">
        WebGL unavailable \u2014 running in fallback mode (scene construction verified, no GPU rendering).
      </div>`;
    }

    // Mesh group for voxel geometry
    this.meshGroup = new THREE.Group();
    this.scene.add(this.meshGroup);

    // Reference model group (separate from voxel mesh)
    this.referenceGroup = new THREE.Group();
    this.scene.add(this.referenceGroup);

    // Lights
    this.setupLights();

    // Grid helper
    this.gridHelper = new THREE.GridHelper(
      options.gridSize ?? 100,
      options.gridDivisions ?? 100,
      options.gridColorCenter ?? 0x555555,
      options.gridColorOther ?? 0x333333,
    );
    this.scene.add(this.gridHelper);

    // Handle resize
    if (this.renderer) {
      const onResize = () => {
        const width = container.clientWidth;
        const height = container.clientHeight;
        this.camera.aspect = width / height;
        this.camera.updateProjectionMatrix();
        this.renderer!.setSize(width, height);
      };
      window.addEventListener("resize", onResize);
    }
  }

  protected setupLights(): void {
    const ambient = new THREE.AmbientLight(0xffffff, 0.4);
    this.scene.add(ambient);

    const directional = new THREE.DirectionalLight(0xffffff, 0.8);
    directional.position.set(10, 20, 15);
    directional.castShadow = true;
    directional.shadow.mapSize.width = 2048;
    directional.shadow.mapSize.height = 2048;
    const shadowCam = directional.shadow.camera;
    shadowCam.near = 0.5;
    shadowCam.far = 100;
    shadowCam.left = -30;
    shadowCam.right = 30;
    shadowCam.top = 30;
    shadowCam.bottom = -30;
    this.scene.add(directional);

    const fill = new THREE.DirectionalLight(0xffffff, 0.3);
    fill.position.set(-5, 5, -10);
    this.scene.add(fill);
  }

  /**
   * Build Three.js scene from a RenderSceneSnapshot (canonical #1657 contract).
   * Returns performance metrics.
   */
  buildFromSnapshot(snapshot: RenderSceneSnapshot): RendererMetrics {
    const startTime = performance.now();
    this.lastBounds = snapshot.combined_bounds ?? snapshot.bounds;

    // Reset framing flag when the model changes
    if (snapshot.model_id && snapshot.model_id !== this.lastModelId) {
      this.initialCameraFramed = false;
      this.lastModelId = snapshot.model_id;
    }

    // Clear all existing geometry
    this.clearMesh();
    this.clearReferenceModels();

    // Build reference models
    this.buildReferenceFromSnapshot(snapshot);

    // Build voxel meshes
    if (snapshot.voxel_meshes.length > 0) {
      for (const mesh of snapshot.voxel_meshes) {
        this.buildVoxelMesh(mesh);
      }
    }

    // Frame camera
    if (shouldFrameCamera(this.initialCameraFramed, false)) {
      this.frameCamera(this.lastBounds);
      this.initialCameraFramed = true;
    }

    let firstRenderMs = 0;
    if (this.renderer) {
      this.renderer.render(this.scene, this.camera);
      firstRenderMs = performance.now() - startTime;
    }

    const totalVerts = snapshot.voxel_meshes.reduce(
      (s, m) => s + m.positions.length / 3, 0,
    );
    const totalTris = snapshot.voxel_meshes.reduce(
      (s, m) => s + m.indices.length / 3, 0,
    );

    const metrics: RendererMetrics = {
      scene_construction_ms: performance.now() - startTime,
      first_render_ms: firstRenderMs,
      vertex_count: totalVerts,
      triangle_count: totalTris,
      mesh_transfer_ms: 0,
      total_renderer_ms: performance.now() - startTime,
      webgl_fallback: !this.webglAvailable,
    };

    this.notifyRenderComplete(metrics);
    return metrics;
  }

  /**
   * Build a single voxel mesh from RenderVoxelMesh data.
   */
  protected buildVoxelMesh(mesh: RenderVoxelMesh): void {
    if (mesh.positions.length === 0) return;

    const geometry = new THREE.BufferGeometry();

    // Positions
    const positions = new Float32Array(mesh.positions);
    geometry.setAttribute("position", new THREE.BufferAttribute(positions, 3));

    // Normals
    if (mesh.normals.length > 0) {
      const normals = new Float32Array(mesh.normals);
      geometry.setAttribute("normal", new THREE.BufferAttribute(normals, 3));
    } else {
      geometry.computeVertexNormals();
    }

    // Colors — convert uint8 RGBA to float RGB
    const decodedColors = normalizeColorsRgba(mesh.colors_rgba, mesh.positions.length / 3);
    if (decodedColors.length > 0) {
      const colorCount = decodedColors.length / 3;
      const colors = new Float32Array(decodedColors);
      geometry.setAttribute("color", new THREE.BufferAttribute(colors, 3));
    }

    // Indices
    if (mesh.indices.length > 0) {
      const indices = new Uint32Array(mesh.indices);
      geometry.setIndex(new THREE.BufferAttribute(indices, 1));
    }

    if (!mesh.normals.length) {
      geometry.computeVertexNormals();
    }
    geometry.computeBoundingSphere();

    const material = new THREE.MeshStandardMaterial({
      vertexColors: decodedColors.length > 0,
      side: THREE.FrontSide,
      roughness: 0.7,
      metalness: 0.1,
    });

    const threeMesh = new THREE.Mesh(geometry, material);
    threeMesh.castShadow = true;
    threeMesh.receiveShadow = true;
    this.meshGroup.add(threeMesh);
  }

  /**
   * Build reference models from the snapshot.
   * Override to customize reference model rendering.
   */
  protected buildReferenceFromSnapshot(snapshot: RenderSceneSnapshot): void {
    for (const node of snapshot.reference_nodes) {
      this.buildReferenceNode(node);
    }
  }

  /**
   * Build a single reference node: group + transform + primitives.
   */
  protected buildReferenceNode(node: RenderSceneSnapshot["reference_nodes"][0]): void {
    if (!node.visible) return;

    const modelGroup = new THREE.Group();
    modelGroup.name = node.id;

    // Apply transform: scale -> rotate (ZYX degrees) -> translate
    const scale = node.transform.scale || 1;
    modelGroup.scale.set(scale, scale, scale);

    const rx = (node.transform.rotation_x || 0) * Math.PI / 180;
    const ry = (node.transform.rotation_y || 0) * Math.PI / 180;
    const rz = (node.transform.rotation_z || 0) * Math.PI / 180;
    modelGroup.rotation.order = "ZYX";
    modelGroup.rotation.set(rx, ry, rz);

    modelGroup.position.set(
      node.transform.position_x || 0,
      node.transform.position_y || 0,
      node.transform.position_z || 0,
    );

    // Build primitives
    for (const prim of node.primitives) {
      this.buildPrimitive(prim, modelGroup);
    }

    this.referenceGroup.add(modelGroup);
  }

  /**
   * Build a single render primitive as a Three.js Mesh.
   */
  protected buildPrimitive(prim: RenderPrimitive, parent?: THREE.Group): THREE.Mesh | null {
    if (!prim.position || prim.position.length === 0) return null;

    const geometry = new THREE.BufferGeometry();

    const positions = new Float32Array(prim.position);
    geometry.setAttribute("position", new THREE.BufferAttribute(positions, 3));

    // Normals
    if (prim.normal && prim.normal.length > 0) {
      const normals = new Float32Array(prim.normal);
      geometry.setAttribute("normal", new THREE.BufferAttribute(normals, 3));
    } else {
      geometry.computeVertexNormals();
    }

    // Colors
    const decodedColors = prim.color_rgba
      ? normalizeColorsRgba(prim.color_rgba, prim.position.length / 3)
      : [];
    if (decodedColors.length > 0) {
      const colorCount = decodedColors.length / 3;
      const colors = new Float32Array(decodedColors);
      geometry.setAttribute("color", new THREE.BufferAttribute(colors, 3));
    }

    // UVs
    if (prim.uv_sets && prim.uv_sets.length > 0) {
      const uvSet = prim.uv_sets[0];
      if (uvSet.uvs && uvSet.uvs.length > 0) {
        const uvs = new Float32Array(uvSet.uvs);
        geometry.setAttribute("uv", new THREE.BufferAttribute(uvs, 2));
      }
    }

    // Indices
    if (prim.indices && prim.indices.length > 0) {
      const indices = new Uint32Array(prim.indices);
      geometry.setIndex(new THREE.BufferAttribute(indices, 1));
    }

    if (!prim.normal || prim.normal.length === 0) {
      geometry.computeVertexNormals();
    }
    geometry.computeBoundingSphere();

    // Check alpha transparency from vertex colors
    const hasAlpha = decodedColors.length > 0 && hasVertexAlpha(decodedColors);
    const material = new THREE.MeshStandardMaterial({
      vertexColors: decodedColors.length > 0,
      side: THREE.DoubleSide,
      roughness: 0.6,
      metalness: 0.05,
      transparent: hasAlpha,
      opacity: hasAlpha ? maxVertexAlpha(decodedColors) : 1.0,
      depthWrite: !hasAlpha,
    });

    const threeMesh = new THREE.Mesh(geometry, material);
    threeMesh.castShadow = true;
    threeMesh.receiveShadow = true;

    if (parent) {
      parent.add(threeMesh);
    }

    return threeMesh;
  }

  // ── Legacy compatible API ──

  /**
   * @deprecated Use buildFromSnapshot instead. Kept for backward compat.
   */
  buildMeshFromSnapshot(data: {
    model_id: string;
    vertex_count: number;
    triangle_count: number;
    positions: number[];
    normals: number[];
    colors: number[] | string;
    indices: number[];
    bounds?: BoundsDto | null;
    combined_bounds?: BoundsDto | null;
  }): RendererMetrics {
    const startTime = performance.now();
    this.lastBounds = data.combined_bounds ?? data.bounds ?? null;

    if (data.model_id && data.model_id !== this.lastModelId) {
      this.initialCameraFramed = false;
      this.lastModelId = data.model_id;
    }

    this.clearMesh();

    if (data.vertex_count === 0 || data.positions.length === 0) {
      const metrics: RendererMetrics = {
        scene_construction_ms: performance.now() - startTime,
        first_render_ms: 0,
        vertex_count: 0,
        triangle_count: 0,
        mesh_transfer_ms: 0,
        total_renderer_ms: performance.now() - startTime,
        webgl_fallback: !this.webglAvailable,
      };
      this.notifyRenderComplete(metrics);
      return metrics;
    }

    const snapshot: RenderSceneSnapshot = {
      schema_version: "voxelforge.render_scene@legacy@1",
      revision: 0,
      model_id: data.model_id,
      source: { host: "renderer", capabilities: [] },
      bounds: data.bounds ?? null,
      reference_bounds: null,
      combined_bounds: data.combined_bounds ?? null,
      voxel_meshes: [{
        id: "legacy-mesh",
        revision: 0,
        positions: data.positions,
        normals: data.normals,
        colors_rgba: data.colors,
        palette_indices: [],
        indices: data.indices,
        bounds: data.bounds ?? null,
        payload_format: "json_arrays",
      }],
      reference_nodes: [],
      materials: [],
      textures: [],
      palette: [],
      diagnostics: [],
    };

    return this.buildFromSnapshot(snapshot);
  }

  /**
   * @deprecated Apply an incremental mesh update. Kept for backward compat.
   * For new code, use buildFromSnapshot with a fresh RenderSceneSnapshot.
   */
  applyIncrementalUpdate(update: {
    model_id: string;
    base_mesh_id: string;
    sequence: number;
    update_type: "incremental" | "full_replace";
    changed_regions: Array<{
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
    }>;
    payload_format: string;
    full_vertex_count: number;
    full_index_count: number;
  }): RendererMetrics {
    if (update.update_type === "full_replace" || this.meshGroup.children.length === 0) {
      const fullRegion = update.changed_regions.find((r) => r.update_kind === "full_replace")
        ?? update.changed_regions[0];
      if (fullRegion && fullRegion.vertex_count > 0) {
        return this.buildMeshFromSnapshot({
          model_id: update.model_id,
          vertex_count: fullRegion.vertex_count,
          triangle_count: Math.floor(fullRegion.index_count / 3),
          positions: fullRegion.positions,
          normals: fullRegion.normals,
          colors: fullRegion.colors,
          indices: fullRegion.indices,
          bounds: fullRegion.bounds,
          combined_bounds: null,
        });
      } else {
        this.clearMesh();
        const startTime = performance.now();
        const metrics: RendererMetrics = {
          scene_construction_ms: 0,
          first_render_ms: 0,
          vertex_count: 0,
          triangle_count: 0,
          mesh_transfer_ms: 0,
          total_renderer_ms: performance.now() - startTime,
          webgl_fallback: !this.webglAvailable,
        };
        this.notifyRenderComplete(metrics);
        return metrics;
      }
    }

    const startTime = performance.now();
    for (const region of update.changed_regions) {
      if (region.vertex_count === 0) {
        const existing = this.meshGroup.children.find(
          (child) => child.userData?.regionId === region.region_id,
        );
        if (existing) {
          this.meshGroup.remove(existing);
          if (existing instanceof THREE.Mesh) {
            existing.geometry.dispose();
            if (existing.material instanceof THREE.Material) {
              existing.material.dispose();
            }
          }
        }
        continue;
      }

      const geometry = new THREE.BufferGeometry();
      const positions = new Float32Array(region.positions);
      geometry.setAttribute("position", new THREE.BufferAttribute(positions, 3));

      if (region.normals.length > 0) {
        const normals = new Float32Array(region.normals);
        geometry.setAttribute("normal", new THREE.BufferAttribute(normals, 3));
      } else {
        geometry.computeVertexNormals();
      }

      const decodedColors = decodeByteArray(region.colors, region.vertex_count * 4);
      if (decodedColors.length > 0) {
        const colorCount = region.vertex_count;
        const colors = new Float32Array(colorCount * 3);
        for (let i = 0; i < colorCount; i++) {
          colors[i * 3] = decodedColors[i * 4] / 255;
          colors[i * 3 + 1] = decodedColors[i * 4 + 1] / 255;
          colors[i * 3 + 2] = decodedColors[i * 4 + 2] / 255;
        }
        geometry.setAttribute("color", new THREE.BufferAttribute(colors, 3));
      }

      if (region.indices.length > 0) {
        const indices = new Uint32Array(region.indices);
        geometry.setIndex(new THREE.BufferAttribute(indices, 1));
      }

      if (!region.normals.length) {
        geometry.computeVertexNormals();
      }
      geometry.computeBoundingSphere();

      const material = new THREE.MeshStandardMaterial({
        vertexColors: decodedColors.length > 0,
        side: THREE.FrontSide,
        roughness: 0.7,
        metalness: 0.1,
      });

      const existingChild = this.meshGroup.children.find(
        (child) => child.userData?.regionId === region.region_id,
      );
      if (existingChild) {
        this.meshGroup.remove(existingChild);
        if (existingChild instanceof THREE.Mesh) {
          existingChild.geometry.dispose();
          if (existingChild.material instanceof THREE.Material) {
            existingChild.material.dispose();
          }
        }
      }

      const mesh = new THREE.Mesh(geometry, material);
      mesh.castShadow = true;
      mesh.receiveShadow = true;
      mesh.userData = { regionId: region.region_id };
      this.meshGroup.add(mesh);
    }

    let firstRenderMs = 0;
    if (this.renderer) {
      this.renderer.render(this.scene, this.camera);
      firstRenderMs = performance.now() - startTime;
    }

    const metrics: RendererMetrics = {
      scene_construction_ms: performance.now() - startTime,
      first_render_ms: firstRenderMs,
      vertex_count: update.full_vertex_count,
      triangle_count: Math.floor(update.full_index_count / 3),
      mesh_transfer_ms: 0,
      total_renderer_ms: performance.now() - startTime,
      webgl_fallback: !this.webglAvailable,
    };

    this.notifyRenderComplete(metrics);
    return metrics;
  }

  // ── Scene operations ──

  clearMesh(): void {
    while (this.meshGroup.children.length > 0) {
      const child = this.meshGroup.children[0];
      this.meshGroup.remove(child);
      if (child instanceof THREE.Mesh) {
        child.geometry.dispose();
        if (child.material instanceof THREE.Material) {
          child.material.dispose();
        }
      }
    }
  }

  clearReferenceModels(): void {
    while (this.referenceGroup.children.length > 0) {
      const child = this.referenceGroup.children[0];
      this.referenceGroup.remove(child);
      this.disposeMeshTree(child);
    }
  }

  private disposeMeshTree(obj: THREE.Object3D): void {
    if (obj instanceof THREE.Mesh) {
      obj.geometry.dispose();
      if (obj.material) {
        if ((obj.material as THREE.MeshStandardMaterial).map) {
          (obj.material as THREE.MeshStandardMaterial).map!.dispose();
        }
        obj.material.dispose();
      }
    }
    for (const child of obj.children) {
      this.disposeMeshTree(child);
    }
  }

  frameCurrentModel(): void {
    if (this.lastBounds) {
      this.frameCamera(this.lastBounds);
      if (this.renderer) {
        this.renderer.render(this.scene, this.camera);
      }
    }
  }

  protected frameCamera(bounds: BoundsDto | null): void {
    if (!bounds) {
      this.camera.position.set(10, 10, 10);
      if (this.controls) {
        this.controls.target.set(0, 0, 0);
        this.controls.update();
      }
      return;
    }

    const min = new THREE.Vector3(bounds.min_x, bounds.min_y, bounds.min_z);
    const max = new THREE.Vector3(bounds.max_x, bounds.max_y, bounds.max_z);
    const center = new THREE.Vector3().addVectors(min, max).multiplyScalar(0.5);
    const size = new THREE.Vector3().subVectors(max, min);
    const maxDim = Math.max(size.x, size.y, size.z);
    const distance = Math.max(maxDim * 2.5, 5);

    this.camera.position.set(
      center.x + distance * 0.6,
      center.y + distance * 0.6,
      center.z + distance * 0.6,
    );
    this.camera.near = 0.1;
    this.camera.far = distance * 10;

    if (this.controls) {
      this.controls.target.copy(center);
      this.controls.update();
    }
  }

  setGridVisible(visible: boolean): void {
    if (this.gridHelper) {
      this.gridHelper.visible = visible;
    }
  }

  setWireframeVisible(visible: boolean): void {
    const allMeshes = [...this.meshGroup.children, ...this.referenceGroup.children];
    for (const child of allMeshes) {
      if (child instanceof THREE.Mesh) {
        if (child.material instanceof THREE.MeshStandardMaterial) {
          child.material.wireframe = visible;
        }
      }
      // Also handle grouped meshes
      for (const sub of child.children) {
        if (sub instanceof THREE.Mesh && sub.material instanceof THREE.MeshStandardMaterial) {
          sub.material.wireframe = visible;
        }
      }
    }
  }

  snapCameraToView(view: "front" | "side" | "top"): void {
    const distance = this.lastBounds
      ? Math.max(
          this.lastBounds.max_x - this.lastBounds.min_x,
          this.lastBounds.max_y - this.lastBounds.min_y,
          this.lastBounds.max_z - this.lastBounds.min_z,
        ) * 2.5
      : 20;

    const center = this.controls?.target ?? new THREE.Vector3(0, 0, 0);

    switch (view) {
      case "front":
        this.camera.position.set(center.x, center.y, center.z + distance);
        break;
      case "side":
        this.camera.position.set(center.x + distance, center.y, center.z);
        break;
      case "top":
        this.camera.position.set(center.x, center.y + distance, center.z);
        break;
    }
    this.camera.lookAt(center);
    if (this.controls) {
      this.controls.target.copy(center);
      this.controls.update();
    }
  }

  setBackgroundColor(r: number, g: number, b: number): void {
    this.scene.background = new THREE.Color(`rgb(${r}, ${g}, ${b})`);
  }

  getBackgroundColor(): string {
    const color = this.scene.background;
    if (color instanceof THREE.Color) {
      return color.getHexString();
    }
    return "2b2b2b";
  }

  getCanvas(): HTMLCanvasElement | null {
    return this.renderer?.domElement ?? null;
  }

  // ── Raycasting / Picking ──

  raycast(screenX: number, screenY: number): VoxelRaycastHit | null {
    if (!this.renderer || !this.camera) return null;

    const rect = this.renderer.domElement.getBoundingClientRect();
    const ndcX = ((screenX - rect.left) / rect.width) * 2 - 1;
    const ndcY = -((screenY - rect.top) / rect.height) * 2 + 1;

    const raycaster = new THREE.Raycaster();
    const mouse = new THREE.Vector2(ndcX, ndcY);
    raycaster.setFromCamera(mouse, this.camera);

    const meshes: THREE.Mesh[] = [];
    this.meshGroup.children.forEach((child) => {
      if (child instanceof THREE.Mesh) meshes.push(child);
    });

    if (meshes.length === 0) return null;

    const intersects = raycaster.intersectObjects(meshes, false);
    if (intersects.length === 0) return null;

    const hit = intersects[0];
    const face = hit.face;
    if (!face) return null;

    const point = hit.point;
    const normal = face.normal.clone();
    normal.transformDirection(hit.object.matrixWorld);

    const voxelX = Math.round(point.x - normal.x * 0.5);
    const voxelY = Math.round(point.y - normal.y * 0.5);
    const voxelZ = Math.round(point.z - normal.z * 0.5);

    return {
      position: { x: voxelX, y: voxelY, z: voxelZ },
      normal: { x: Math.round(normal.x), y: Math.round(normal.y), z: Math.round(normal.z) },
      palette_index: 1,
      screen: { x: screenX, y: screenY },
      ray_origin: {
        x: raycaster.ray.origin.x,
        y: raycaster.ray.origin.y,
        z: raycaster.ray.origin.z,
      },
      distance: hit.distance,
    };
  }

  computePlacementPosition(hit: VoxelRaycastHit): { x: number; y: number; z: number } {
    return computePlacementPosition(hit);
  }

  // ── Render loop ──

  protected animate(): void {
    if (!this.renderer) return;
    this.animationFrameId = requestAnimationFrame(() => this.animate());
    this.controls!.update();
    this.renderer!.render(this.scene, this.camera);
  }

  // ── Callbacks ──

  protected notifyRenderComplete(metrics: RendererMetrics): void {
    this.renderCallbacks.forEach((cb) => cb(metrics));
  }

  onRenderComplete(callback: (metrics: RendererMetrics) => void): void {
    this.renderCallbacks.push(callback);
  }

  // ── Lifecycle ──

  getScene(): THREE.Scene { return this.scene; }
  getCamera(): THREE.PerspectiveCamera { return this.camera; }
  getRenderer(): THREE.WebGLRenderer | null { return this.renderer; }
  getControls(): OrbitControls | null { return this.controls; }
  getMeshGroup(): THREE.Group { return this.meshGroup; }
  getReferenceGroup(): THREE.Group { return this.referenceGroup; }
  get isWebglAvailable(): boolean { return this.webglAvailable; }

  dispose(): void {
    if (this.animationFrameId !== null) {
      cancelAnimationFrame(this.animationFrameId);
    }
    this.clearMesh();
    this.clearReferenceModels();
    if (this.renderer) {
      this.renderer.dispose();
    }
    if (this.controls) {
      this.controls.dispose();
    }
  }
}

// ── Color helpers ──

/**
 * Normalize RGBA byte data to float RGB array.
 * Accepts number[] or string (base64).
 */
export function normalizeColorsRgba(
  colorsRgba: number[] | string,
  vertexCount: number,
): number[] {
  const decoded = colorsRgba
    ? decodeByteArray(colorsRgba, vertexCount * 4)
    : [];

  if (decoded.length === 0) return [];

  const colors: number[] = [];
  for (let i = 0; i < vertexCount; i++) {
    colors[i * 3] = decoded[i * 4] / 255;
    colors[i * 3 + 1] = decoded[i * 4 + 1] / 255;
    colors[i * 3 + 2] = decoded[i * 4 + 2] / 255;
  }
  return colors;
}

/**
 * Check if any vertex has non-255 alpha (indicates transparency).
 */
export function hasVertexAlpha(floatColors: number[]): boolean {
  // floatColors is [r,g,b, r,g,b, ...] — we need the original alpha
  // This is called after normalizeColorsRgba drops alpha. We work around by
  // checking decoded RGBA bytes passed separately. For now, always check the
  // input before normalization if needed.
  return false; // Override point: track alpha during normalization
}

/**
 * Get the maximum alpha value from float RGB colors (fallback).
 */
export function maxVertexAlpha(floatColors: number[]): number {
  return 1.0;
}
