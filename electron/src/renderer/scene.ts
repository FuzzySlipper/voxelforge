/**
 * Three.js scene manager for VoxelForge static mesh rendering.
 * Owns the scene, camera, lights, and mesh construction.
 * Does NOT own model/editor mutations — rendering and presentation only.
 */

import * as THREE from "three";
import { OrbitControls } from "three/examples/jsm/controls/OrbitControls.js";

/** Shape of the mesh snapshot response from the C# sidecar. */
export interface MeshSnapshotData {
  model_id: string;
  mesh_id: string;
  format: string;
  vertex_count: number;
  index_count: number;
  triangle_count: number;
  positions: number[];
  normals: number[];
  colors: number[];
  palette_indices?: number[];
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

/** Shape of the palette response from the C# sidecar. */
export interface PaletteData {
  palette_id: string;
  entries: { index: number; name: string; color: string; a: number; visible: boolean }[];
  entry_count: number;
}

/** Shape of a mesh update event from the C# sidecar. */
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

/** Shape of a single region update within a mesh update event. */
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
  colors: number[];
  palette_indices?: number[];
  indices: number[];
}

/** Shape of a palette update event from the C# sidecar. */
export interface PaletteUpdateEventData {
  model_id: string;
  sequence: number;
  update_type: "full_replace" | "partial";
  entries: { index: number; name: string; color: string; a: number; visible: boolean }[];
  entry_count: number;
}

/** Performance metrics captured in the renderer. */
export interface RendererMetrics {
  scene_construction_ms: number;
  first_render_ms: number;
  vertex_count: number;
  triangle_count: number;
  mesh_transfer_ms: number;
  total_renderer_ms: number;
  /** When true, the renderer fell back to no-GPU mode because WebGL was unavailable. */
  webgl_fallback?: boolean;
}

export class VoxelForgeScene {
  private scene: THREE.Scene;
  private camera: THREE.PerspectiveCamera;
  private renderer: THREE.WebGLRenderer | null = null;
  private controls: OrbitControls | null = null;
  private meshGroup: THREE.Group;
  private animationFrameId: number | null = null;
  private renderCallbacks: ((metrics: RendererMetrics) => void)[] = [];
  private container: HTMLElement;
  private webglAvailable: boolean;

  constructor(container: HTMLElement) {
    this.container = container;
    this.webglAvailable = true;

    // Scene
    this.scene = new THREE.Scene();
    this.scene.background = new THREE.Color(0x2b2b2b);

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

      // Orbit controls for camera pan/zoom/orbit
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
      console.warn("[scene] WebGL not available; renderer will operate in fallback mode:", err);
      this.webglAvailable = false;
      // Show a message in the container
      container.innerHTML = `<div style="color: #aaa; padding: 20px; font-family: monospace;">
        WebGL unavailable — running in fallback mode (scene construction verified, no GPU rendering).
      </div>`;
    }

    // Mesh group
    this.meshGroup = new THREE.Group();
    this.scene.add(this.meshGroup);

    // Lights
    this.setupLights();

    // Grid helper
    const grid = new THREE.GridHelper(100, 100, 0x555555, 0x333333);
    this.scene.add(grid);

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

  private setupLights(): void {
    // Ambient light for base illumination
    const ambient = new THREE.AmbientLight(0xffffff, 0.4);
    this.scene.add(ambient);

    // Directional light simulating sun
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

    // Fill light from below-side
    const fill = new THREE.DirectionalLight(0xffffff, 0.3);
    fill.position.set(-5, 5, -10);
    this.scene.add(fill);
  }

  /**
   * Build Three.js mesh from the C# sidecar mesh snapshot data.
   * Returns performance metrics for the construction process.
   * Works in both WebGL and fallback mode.
   */
  buildMeshFromSnapshot(data: MeshSnapshotData): RendererMetrics {
    const startTime = performance.now();

    // Clear previous mesh
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

    if (data.vertex_count === 0 || data.positions.length === 0) {
      // Empty model — nothing to render
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

    const constructionStart = performance.now();

    // Build geometry
    const geometry = new THREE.BufferGeometry();

    // Positions: flat array [x0,y0,z0, x1,y1,z1, ...]
    const positions = new Float32Array(data.positions);
    geometry.setAttribute("position", new THREE.BufferAttribute(positions, 3));

    // Normals: flat array [nx0,ny0,nz0, ...]
    if (data.normals.length > 0) {
      const normals = new Float32Array(data.normals);
      geometry.setAttribute("normal", new THREE.BufferAttribute(normals, 3));
    } else {
      geometry.computeVertexNormals();
    }

    // Colors: flat array [r0,g0,b0,a0, ...] — convert from uint8 RGBA
    if (data.colors.length > 0) {
      const colorCount = data.vertex_count;
      const colors = new Float32Array(colorCount * 3);
      for (let i = 0; i < colorCount; i++) {
        colors[i * 3] = data.colors[i * 4] / 255;
        colors[i * 3 + 1] = data.colors[i * 4 + 1] / 255;
        colors[i * 3 + 2] = data.colors[i * 4 + 2] / 255;
      }
      geometry.setAttribute("color", new THREE.BufferAttribute(colors, 3));
    }

    // Indices
    if (data.indices.length > 0) {
      const indices = new Uint32Array(data.indices);
      geometry.setIndex(new THREE.BufferAttribute(indices, 1));
    }

    // Compute normals if not provided
    if (!data.normals.length) {
      geometry.computeVertexNormals();
    }

    geometry.computeBoundingSphere();

    const constructionEnd = performance.now();

    // Material — use vertex colors if available, otherwise use a default material
    // Check winding/culling: VoxelForge greedy mesher produces CCW faces for front-facing,
    // which aligns with Three.js default FrontSide culling.
    const material = new THREE.MeshStandardMaterial({
      vertexColors: data.colors.length > 0,
      side: THREE.FrontSide,
      roughness: 0.7,
      metalness: 0.1,
    });

    const mesh = new THREE.Mesh(geometry, material);
    mesh.castShadow = true;
    mesh.receiveShadow = true;
    this.meshGroup.add(mesh);

    // Frame camera on the model bounds
    this.frameCamera(data);

    let firstRenderMs = 0;
    if (this.renderer) {
      // Force a render to measure first-render time (requires WebGL)
      this.renderer.render(this.scene, this.camera);
      firstRenderMs = performance.now() - constructionEnd;
    }

    const metrics: RendererMetrics = {
      scene_construction_ms: constructionEnd - constructionStart,
      first_render_ms: firstRenderMs,
      vertex_count: data.vertex_count,
      triangle_count: data.triangle_count,
      mesh_transfer_ms: 0, // Measured on the main process side
      total_renderer_ms: performance.now() - startTime,
      webgl_fallback: !this.webglAvailable,
    };

    this.notifyRenderComplete(metrics);
    return metrics;
  }

  /**
   * Frame the camera to show the entire model with comfortable margins.
   */
  private frameCamera(data: MeshSnapshotData): void {
    if (!data.bounds) {
      // Default camera position for empty/unbounded models
      this.camera.position.set(10, 10, 10);
      if (this.controls) {
        this.controls.target.set(0, 0, 0);
        this.controls.update();
      }
      return;
    }

    const min = new THREE.Vector3(data.bounds.min_x, data.bounds.min_y, data.bounds.min_z);
    const max = new THREE.Vector3(data.bounds.max_x, data.bounds.max_y, data.bounds.max_z);
    const center = new THREE.Vector3().addVectors(min, max).multiplyScalar(0.5);
    const size = new THREE.Vector3().subVectors(max, min);
    const maxDim = Math.max(size.x, size.y, size.z);

    // Position camera at a 45-degree angle, far enough to see the whole model
    const distance = maxDim * 2.5;
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

  /**
   * Apply an incremental mesh update by replacing buffers for dirty regions.
   * Works in both WebGL and fallback mode.
   * Returns performance metrics for the update process.
   */
  applyIncrementalUpdate(update: MeshUpdateEventData): RendererMetrics {
    const startTime = performance.now();

    if (update.update_type === "full_replace" || this.meshGroup.children.length === 0) {
      // For full replace, rebuild the entire mesh from the first region (or all regions)
      const fullRegion = update.changed_regions.find((r) => r.update_kind === "full_replace")
        ?? update.changed_regions[0];

      if (fullRegion && fullRegion.vertex_count > 0) {
        // Convert region data to a full mesh snapshot for buildMeshFromSnapshot
        const snapshotData: MeshSnapshotData = {
          model_id: update.model_id,
          mesh_id: update.base_mesh_id,
          format: update.payload_format,
          vertex_count: fullRegion.vertex_count,
          index_count: fullRegion.index_count,
          triangle_count: Math.floor(fullRegion.index_count / 3),
          positions: fullRegion.positions,
          normals: fullRegion.normals,
          colors: fullRegion.colors,
          palette_indices: fullRegion.palette_indices,
          indices: fullRegion.indices,
          bounds: fullRegion.bounds,
        };

        return this.buildMeshFromSnapshot(snapshotData);
      } else {
        // Empty full replace — clear the scene
        this.clearMesh();
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

    // Incremental update: replace region meshes
    const constructionStart = performance.now();

    for (const region of update.changed_regions) {
      if (region.vertex_count === 0) {
        // Remove existing region mesh if present
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

      // Build or replace region geometry
      const geometry = new THREE.BufferGeometry();

      const positions = new Float32Array(region.positions);
      geometry.setAttribute("position", new THREE.BufferAttribute(positions, 3));

      if (region.normals.length > 0) {
        const normals = new Float32Array(region.normals);
        geometry.setAttribute("normal", new THREE.BufferAttribute(normals, 3));
      } else {
        geometry.computeVertexNormals();
      }

      if (region.colors.length > 0) {
        const colorCount = region.vertex_count;
        const colors = new Float32Array(colorCount * 3);
        for (let i = 0; i < colorCount; i++) {
          colors[i * 3] = region.colors[i * 4] / 255;
          colors[i * 3 + 1] = region.colors[i * 4 + 1] / 255;
          colors[i * 3 + 2] = region.colors[i * 4 + 2] / 255;
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
        vertexColors: region.colors.length > 0,
        side: THREE.FrontSide,
        roughness: 0.7,
        metalness: 0.1,
      });

      // Remove old region mesh if present
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

    const constructionEnd = performance.now();

    let firstRenderMs = 0;
    if (this.renderer) {
      this.renderer.render(this.scene, this.camera);
      firstRenderMs = performance.now() - constructionEnd;
    }

    const metrics: RendererMetrics = {
      scene_construction_ms: constructionEnd - constructionStart,
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

  /**
   * Clear all mesh geometry from the scene.
   */
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

  private animate(): void {
    if (!this.renderer) return;
    this.animationFrameId = requestAnimationFrame(() => this.animate());
    this.controls!.update();
    this.renderer!.render(this.scene, this.camera);
  }

  private notifyRenderComplete(metrics: RendererMetrics): void {
    this.renderCallbacks.forEach((cb) => cb(metrics));
  }

  /**
   * Register a callback for render completion metrics.
   */
  onRenderComplete(callback: (metrics: RendererMetrics) => void): void {
    this.renderCallbacks.push(callback);
  }

  dispose(): void {
    if (this.animationFrameId !== null) {
      cancelAnimationFrame(this.animationFrameId);
    }
    // Dispose geometries and materials
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
    if (this.renderer) {
      this.renderer.dispose();
    }
    if (this.controls) {
      this.controls.dispose();
    }
  }
}