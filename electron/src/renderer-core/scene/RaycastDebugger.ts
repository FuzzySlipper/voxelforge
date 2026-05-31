/**
 * RaycastDebugger — Temporary raycast debug overlay for voxel picking diagnostics.
 *
 * Manages:
 *   - Text diagnostics overlay (HTML div) for recent click/raycast events
 *   - Three.js scene debug drawings (ray line, hit point, normal, voxel highlight)
 *   - Auto-expiry after ~10 seconds
 *   - Bounded accumulation (max 10 events)
 *
 * Compatible with VoxelForgeScene by operating on the same scene/camera references.
 */

import * as THREE from "three";
import type { VoxelRaycastHit } from "../../shared/compute-placement";

// ── Constants ──

const MAX_DEBUG_EVENTS = 10;
const DEBUG_EXPIRY_MS = 10_000;
const DEBUG_CLEANUP_INTERVAL_MS = 1_000;
const RAY_LINE_COLOR = 0x00ff88;
const HIT_POINT_COLOR = 0xff6600;
const NORMAL_COLOR = 0xffff00;
const VOXEL_HIGHLIGHT_COLOR = 0x00aaff;
const VOXEL_HALF_EXTENT = 0.5;

// ── Types ──

export interface RaycastDebugEvent {
  /** Timestamp when the event was captured. */
  timestamp: number;
  /** Raw screen/browser coordinates (CSS pixels). */
  screenX: number;
  screenY: number;
  /** Canvas-local client coordinates (CSS pixels). */
  clientX: number;
  clientY: number;
  /** Normalized Device Coordinates [-1, 1]. */
  ndcX: number;
  ndcY: number;
  /** Device Pixel Ratio used by the renderer. */
  dpr: number;
  /** Canvas bounding rect (CSS pixels). */
  canvasRect: { left: number; top: number; width: number; height: number };
  /** Ray origin in world space. */
  rayOrigin: { x: number; y: number; z: number };
  /** Ray direction in world space. */
  rayDirection: { x: number; y: number; z: number };
  /** Whether a voxel was hit. */
  hit: boolean;
  /** Hit object type/name. */
  hitObjectType?: string;
  hitObjectId?: string;
  /** Hit distance along ray. */
  hitDistance?: number;
  /** World-space hit point. */
  hitPoint?: { x: number; y: number; z: number };
  /** Hit normal in world space. */
  hitNormal?: { x: number; y: number; z: number };
  /** Computed voxel coordinate (integer grid position). */
  voxelCoord?: { x: number; y: number; z: number };
  /** Computed placement position (voxel + normal). */
  placementCoord?: { x: number; y: number; z: number };
  /** The full VoxelRaycastHit for reference. */
  rawHit?: VoxelRaycastHit;
}

// ── RaycastDebugger class ──

export class RaycastDebugger {
  private events: RaycastDebugEvent[] = [];
  private enabled = false;
  private container: HTMLElement;
  private scene: THREE.Scene;
  private camera: THREE.PerspectiveCamera;
  private overlayEl: HTMLDivElement | null = null;
  private debugGroup: THREE.Group;
  private cleanupTimer: ReturnType<typeof setInterval> | null = null;

  constructor(
    container: HTMLElement,
    scene: THREE.Scene,
    camera: THREE.PerspectiveCamera,
  ) {
    this.container = container;
    this.scene = scene;
    this.camera = camera;

    // Group for all debug drawings, added on top of everything
    this.debugGroup = new THREE.Group();
    this.debugGroup.name = "raycast-debug-overlay";
    this.scene.add(this.debugGroup);
  }

  /** Enable or disable the debug overlay. */
  setEnabled(enabled: boolean): void {
    if (enabled === this.enabled) return;
    this.enabled = enabled;

    if (enabled) {
      this.createOverlayElement();
      this.startCleanupTimer();
    } else {
      this.clearAll();
      this.destroyOverlayElement();
      this.stopCleanupTimer();
    }
  }

  get isEnabled(): boolean {
    return this.enabled;
  }

  /** Record a new raycast debug event and update drawings. */
  recordEvent(event: RaycastDebugEvent): void {
    if (!this.enabled) return;

    this.events.push(event);
    if (this.events.length > MAX_DEBUG_EVENTS) {
      const removed = this.events.shift()!;
      this.removeDebugDrawings(removed.timestamp);
    }

    this.addDebugDrawings(event);
    this.updateOverlayText();
  }

  /** Clear all recorded events and debug drawings. */
  clear(): void {
    if (!this.enabled) return;
    this.clearAll();
    this.updateOverlayText();
  }

  /** Get the array of debug events for diagnostics. */
  getEvents(): RaycastDebugEvent[] {
    return [...this.events];
  }

  /** Remove expired events (called periodically by cleanup timer). */
  private removeExpired(): void {
    if (!this.enabled) return;
    const now = performance.now();
    const before = this.events.length;
    this.events = this.events.filter((e) => (now - e.timestamp) < DEBUG_EXPIRY_MS);
    if (this.events.length < before) {
      // Clean up orphaned drawings for removed events
      this.purgeOrphanedDrawings();
      this.updateOverlayText();
    }
  }

  // ── Overlay DOM element ──

  private createOverlayElement(): void {
    if (this.overlayEl) return;
    this.overlayEl = document.createElement("div");
    this.overlayEl.id = "raycast-debug-overlay";
    this.overlayEl.style.cssText = [
      "position: absolute",
      "right: 10px",
      "bottom: 40px",
      "z-index: 1000",
      "pointer-events: none",
      "font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace",
      "font-size: 11px",
      "line-height: 1.35",
      "color: #b8ffb8",
      "background: rgba(0, 0, 0, 0.78)",
      "border: 1px solid rgba(0, 255, 136, 0.25)",
      "border-radius: 6px",
      "padding: 8px 10px",
      "max-width: 460px",
      "max-height: 360px",
      "overflow: hidden",
    ].join(";");
    this.container.appendChild(this.overlayEl);
  }

  private destroyOverlayElement(): void {
    if (this.overlayEl && this.overlayEl.parentNode) {
      this.overlayEl.parentNode.removeChild(this.overlayEl);
    }
    this.overlayEl = null;
  }

  private updateOverlayText(): void {
    if (!this.overlayEl) return;
    if (this.events.length === 0) {
      this.overlayEl.textContent = "[raycast debug: no events]";
      return;
    }

    // Show only the most recent event in full detail, plus a count
    const latest = this.events[this.events.length - 1];
    const lines: string[] = [];
    lines.push(`─ Raycast Debug (${this.events.length} events) ─`);

    // Coordinate chain
    lines.push(` screen: (${latest.screenX}, ${latest.screenY})`);
    lines.push(` client: (${latest.clientX}, ${latest.clientY})`);
    lines.push(` canvas rect: L=${latest.canvasRect.left} T=${latest.canvasRect.top} W=${latest.canvasRect.width} H=${latest.canvasRect.height}`);
    lines.push(` dpr: ${latest.dpr}`);
    lines.push(` ndc: (${latest.ndcX.toFixed(4)}, ${latest.ndcY.toFixed(4)})`);

    // Ray
    const ro = latest.rayOrigin;
    const rd = latest.rayDirection;
    lines.push(` ray origin: (${ro.x.toFixed(2)}, ${ro.y.toFixed(2)}, ${ro.z.toFixed(2)})`);
    lines.push(` ray dir: (${rd.x.toFixed(4)}, ${rd.y.toFixed(4)}, ${rd.z.toFixed(4)})`);

    // Hit info
    if (latest.hit && latest.hitPoint) {
      lines.push(` hit: ${latest.hitObjectType ?? "voxel"}`);
      lines.push(` hit dist: ${latest.hitDistance?.toFixed(3)}`);
      const hp = latest.hitPoint;
      lines.push(` hit point: (${hp.x.toFixed(4)}, ${hp.y.toFixed(4)}, ${hp.z.toFixed(4)})`);
      if (latest.hitNormal) {
        const hn = latest.hitNormal;
        lines.push(` hit normal: (${hn.x.toFixed(2)}, ${hn.y.toFixed(2)}, ${hn.z.toFixed(2)})`);
      }
      if (latest.voxelCoord) {
        const vc = latest.voxelCoord;
        lines.push(` voxel coord: (${vc.x}, ${vc.y}, ${vc.z})`);
      }
      if (latest.placementCoord) {
        const pc = latest.placementCoord;
        lines.push(` placement: (${pc.x}, ${pc.y}, ${pc.z})`);
      }
    } else {
      lines.push(" hit: none");
    }

    this.overlayEl.textContent = lines.join("\n");
  }

  // ── Three.js debug drawings ──

  private addDebugDrawings(event: RaycastDebugEvent): void {
    if (!event.hit || !event.hitPoint || !event.hitNormal) return;

    const group = new THREE.Group();
    group.name = `debug-${event.timestamp}`;

    // 1. Ray segment: from near plane along ray to hit point
    const camPos = new THREE.Vector3(
      event.rayOrigin.x, event.rayOrigin.y, event.rayOrigin.z,
    );
    const hitPos = new THREE.Vector3(
      event.hitPoint.x, event.hitPoint.y, event.hitPoint.z,
    );

    const rayLineGeo = new THREE.BufferGeometry().setFromPoints([camPos, hitPos]);
    const rayLineMat = new THREE.LineBasicMaterial({
      color: RAY_LINE_COLOR,
      transparent: true,
      opacity: 0.6,
    });
    const rayLine = new THREE.Line(rayLineGeo, rayLineMat);
    group.add(rayLine);

    // 2. Hit point marker: small sphere
    const sphereGeo = new THREE.SphereGeometry(0.08, 8, 8);
    const sphereMat = new THREE.MeshBasicMaterial({ color: HIT_POINT_COLOR });
    const sphere = new THREE.Mesh(sphereGeo, sphereMat);
    sphere.position.copy(hitPos);
    group.add(sphere);

    // 3. Normal indicator: short arrow from hit point along normal
    if (event.hitNormal) {
      const normalDir = new THREE.Vector3(
        event.hitNormal.x, event.hitNormal.y, event.hitNormal.z,
      ).normalize();
      const arrowLen = 0.4;
      const arrow = new THREE.ArrowHelper(normalDir, hitPos, arrowLen, NORMAL_COLOR, 0.15, 0.08);
      group.add(arrow);
    }

    // 4. Target voxel outline: wireframe box at the computed voxel position
    if (event.voxelCoord) {
      const vc = event.voxelCoord;
      const boxGeo = new THREE.BoxGeometry(1.01, 1.01, 1.01);
      const edges = new THREE.EdgesGeometry(boxGeo);
      const edgeMat = new THREE.LineBasicMaterial({
        color: VOXEL_HIGHLIGHT_COLOR,
        transparent: true,
        opacity: 0.7,
      });
      const wireframe = new THREE.LineSegments(edges, edgeMat);
      wireframe.position.set(vc.x, vc.y, vc.z);
      group.add(wireframe);
    }

    this.debugGroup.add(group);
  }

  private removeDebugDrawings(timestamp: number): void {
    const targetName = `debug-${timestamp}`;
    const child = this.debugGroup.getObjectByName(targetName);
    if (child) {
      this.disposeDebugGroup(child);
      this.debugGroup.remove(child);
    }
  }

  private purgeOrphanedDrawings(): void {
    const validTimestamps = new Set(this.events.map((e) => `debug-${e.timestamp}`));
    const toRemove: THREE.Object3D[] = [];
    for (const child of this.debugGroup.children) {
      if (child.name && child.name.startsWith("debug-") && !validTimestamps.has(child.name)) {
        toRemove.push(child);
      }
    }
    for (const child of toRemove) {
      this.disposeDebugGroup(child);
      this.debugGroup.remove(child);
    }
  }

  private clearAll(): void {
    this.events = [];
    while (this.debugGroup.children.length > 0) {
      const child = this.debugGroup.children[0];
      this.disposeDebugGroup(child);
      this.debugGroup.remove(child);
    }
  }

  private disposeDebugGroup(obj: THREE.Object3D): void {
    obj.traverse((child: THREE.Object3D) => {
      if (child instanceof THREE.Mesh) {
        child.geometry?.dispose();
        if (child.material) {
          (child.material as THREE.Material).dispose();
        }
      }
      if (child instanceof THREE.Line || child instanceof THREE.LineSegments) {
        child.geometry?.dispose();
        if (child.material) {
          (child.material as THREE.Material).dispose();
        }
      }
    });
  }

  // ── Cleanup timer ──

  private startCleanupTimer(): void {
    if (this.cleanupTimer) return;
    this.cleanupTimer = setInterval(() => {
      this.removeExpired();
    }, DEBUG_CLEANUP_INTERVAL_MS);
  }

  private stopCleanupTimer(): void {
    if (this.cleanupTimer !== null) {
      clearInterval(this.cleanupTimer);
      this.cleanupTimer = null;
    }
  }

  /** Dispose all resources. Call when the scene is destroyed. */
  dispose(): void {
    this.setEnabled(false);
    this.scene.remove(this.debugGroup);
    this.disposeDebugGroup(this.debugGroup);
  }
}

// ── Pure utility: compute voxel coordinate from hit point and normal ──

const VOXEL_EPSILON = 1e-9;

/**
 * Compute the voxel grid coordinate from a raycast hit point and face normal.
 *
 * Convention: voxel centers are at integer coordinates. Each voxel occupies
 * space from (center - 0.5) to (center + 0.5) on each axis.
 *
 * The hit point P is on the face of the voxel at offset ±0.5 from center
 * along the face normal N. So the center C = P - N * 0.5, rounded to integer.
 *
 * Uses Math.round which handles IEEE 754 precision for typical voxel scenes.
 * Returns positive zero (0) instead of -0 for consistent equality checks.
 */
export function computeVoxelFromHit(
  point: { x: number; y: number; z: number },
  normal: { x: number; y: number; z: number },
  halfExtent: number = VOXEL_HALF_EXTENT,
): { x: number; y: number; z: number } {
  const x = Math.round(point.x - normal.x * halfExtent);
  const y = Math.round(point.y - normal.y * halfExtent);
  const z = Math.round(point.z - normal.z * halfExtent);
  return {
    x: x === 0 ? 0 : x,
    y: y === 0 ? 0 : y,
    z: z === 0 ? 0 : z,
  };
}

/**
 * Compute client-space coordinates (CSS pixels) from a MouseEvent,
 * accounting for the canvas element's bounding rect.
 * Returns { clientX, clientY, ndcX, ndcY, canvasRect, dpr }.
 */
export function computeScreenToNDC(
  clientX: number,
  clientY: number,
  canvas: HTMLCanvasElement,
): {
  clientX: number;
  clientY: number;
  ndcX: number;
  ndcY: number;
  canvasRect: { left: number; top: number; width: number; height: number };
  dpr: number;
} {
  const rect = canvas.getBoundingClientRect();
  const canvasX = clientX - rect.left;
  const canvasY = clientY - rect.top;
  const ndcX = (canvasX / rect.width) * 2 - 1;
  const ndcY = -(canvasY / rect.height) * 2 + 1;
  return {
    clientX: canvasX,
    clientY: canvasY,
    ndcX,
    ndcY,
    canvasRect: {
      left: rect.left,
      top: rect.top,
      width: rect.width,
      height: rect.height,
    },
    dpr: window.devicePixelRatio,
  };
}

/**
 * Build a RaycastDebugEvent for a successful voxel hit.
 */
export function buildRaycastDebugEvent(
  screenX: number,
  screenY: number,
  hit: VoxelRaycastHit,
  ndcData: ReturnType<typeof computeScreenToNDC>,
  rayDirection: { x: number; y: number; z: number },
  hitObjectType?: string,
  hitObjectId?: string,
): RaycastDebugEvent {
  const computedVoxel = computeVoxelFromHit(hit.position, hit.normal);
  return {
    timestamp: performance.now(),
    screenX,
    screenY,
    clientX: ndcData.clientX,
    clientY: ndcData.clientY,
    ndcX: ndcData.ndcX,
    ndcY: ndcData.ndcY,
    dpr: ndcData.dpr,
    canvasRect: ndcData.canvasRect,
    rayOrigin: { ...hit.ray_origin },
    rayDirection: { ...rayDirection },
    hit: true,
    hitObjectType,
    hitObjectId,
    hitDistance: hit.distance,
    hitPoint: { x: hit.position.x, y: hit.position.y, z: hit.position.z },
    hitNormal: { ...hit.normal },
    voxelCoord: computedVoxel,
    placementCoord: {
      x: computedVoxel.x + hit.normal.x,
      y: computedVoxel.y + hit.normal.y,
      z: computedVoxel.z + hit.normal.z,
    },
    rawHit: { ...hit },
  };
}

/**
 * Build a RaycastDebugEvent for a miss (no voxel hit).
 */
export function buildRaycastMissDebugEvent(
  screenX: number,
  screenY: number,
  ndcData: ReturnType<typeof computeScreenToNDC>,
  rayOrigin: { x: number; y: number; z: number },
  rayDirection: { x: number; y: number; z: number },
  cameraFar: number,
): RaycastDebugEvent {
  return {
    timestamp: performance.now(),
    screenX,
    screenY,
    clientX: ndcData.clientX,
    clientY: ndcData.clientY,
    ndcX: ndcData.ndcX,
    ndcY: ndcData.ndcY,
    dpr: ndcData.dpr,
    canvasRect: ndcData.canvasRect,
    rayOrigin: { x: rayOrigin.x, y: rayOrigin.y, z: rayOrigin.z },
    rayDirection: { x: rayDirection.x, y: rayDirection.y, z: rayDirection.z },
    hit: false,
    hitDistance: cameraFar,
  };
}
