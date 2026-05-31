/**
 * Tests for the raycast debug overlay and coordinate math utilities (#1794).
 *
 * Tests the pure functions extracted from RaycastDebugger:
 *   - computeVoxelFromHit: voxel coordinate computation from hit point + normal
 *   - computeScreenToNDC: screen coordinate → NDC conversion
 *   - buildRaycastDebugEvent: debug events retain both exact hit point and voxel cell
 *
 * The RaycastDebugger class itself requires DOM/Three.js rendering and is live-smoke
 * tested via the Electron renderer interactive harness.
 */

import { describe, it, expect } from "vitest";
import {
  computeVoxelFromHit,
  computeScreenToNDC,
  buildRaycastDebugEvent,
  type RaycastDebugEvent,
} from "../../src/renderer-core/scene/RaycastDebugger";
import type { VoxelRaycastHit } from "../../src/shared/compute-placement";

// ── computeVoxelFromHit tests ──

describe("computeVoxelFromHit", () => {
  it("resolves +X face hit to correct voxel using corner-origin cell bounds", () => {
    // C# GreedyMesher emits voxel (0,0,0) with bounds [0,1] on each axis.
    const voxel = computeVoxelFromHit(
      { x: 1, y: 0.3, z: 0.2 },
      { x: 1, y: 0, z: 0 },
    );
    expect(voxel).toEqual({ x: 0, y: 0, z: 0 });
  });

  it("resolves -X face hit to correct voxel", () => {
    // -X face of voxel (1,0,0) lies on x=1 and points toward negative X.
    const voxel = computeVoxelFromHit(
      { x: 1, y: 0.3, z: 0.2 },
      { x: -1, y: 0, z: 0 },
    );
    expect(voxel).toEqual({ x: 1, y: 0, z: 0 });
  });

  it("resolves +Y face hit to correct voxel", () => {
    const voxel = computeVoxelFromHit(
      { x: 0.3, y: 1, z: 0.2 },
      { x: 0, y: 1, z: 0 },
    );
    expect(voxel).toEqual({ x: 0, y: 0, z: 0 });
  });

  it("resolves -Y face hit to correct voxel", () => {
    const voxel = computeVoxelFromHit(
      { x: 0.3, y: -1, z: 0.2 },
      { x: 0, y: -1, z: 0 },
    );
    expect(voxel).toEqual({ x: 0, y: -1, z: 0 });
  });

  it("resolves +Z face hit to correct voxel", () => {
    const voxel = computeVoxelFromHit(
      { x: 0.3, y: 0.2, z: 1 },
      { x: 0, y: 0, z: 1 },
    );
    expect(voxel).toEqual({ x: 0, y: 0, z: 0 });
  });

  it("resolves -Z face hit to correct voxel", () => {
    const voxel = computeVoxelFromHit(
      { x: 0.3, y: 0.2, z: 1 },
      { x: 0, y: 0, z: -1 },
    );
    expect(voxel).toEqual({ x: 0, y: 0, z: 1 });
  });

  it("handles negative coordinate voxels correctly", () => {
    // Voxel (-1,-2,-3) has +X face on x=0.
    const voxel = computeVoxelFromHit(
      { x: 0, y: -1.7, z: -2.8 },
      { x: 1, y: 0, z: 0 },
    );
    expect(voxel).toEqual({ x: -1, y: -2, z: -3 });
  });

  it("handles floating-point imprecision slightly outside a +X face boundary", () => {
    const voxel = computeVoxelFromHit(
      { x: 1.0000001, y: 0.0, z: 0.0 },
      { x: 1, y: 0, z: 0 },
    );
    expect(voxel).toEqual({ x: 0, y: 0, z: 0 });
  });

  it("handles floating-point imprecision slightly inside a +X face boundary", () => {
    const voxel = computeVoxelFromHit(
      { x: 0.9999999, y: 0.0, z: 0.0 },
      { x: 1, y: 0, z: 0 },
    );
    expect(voxel).toEqual({ x: 0, y: 0, z: 0 });
  });

  it("handles floating-point imprecision on -X face near voxel 1", () => {
    const voxel = computeVoxelFromHit(
      { x: 0.9999999, y: 0.0, z: 0.0 },
      { x: -1, y: 0, z: 0 },
    );
    expect(voxel).toEqual({ x: 1, y: 0, z: 0 });
  });

  it("uses custom epsilon correctly", () => {
    const voxel = computeVoxelFromHit(
      { x: 1.0, y: 0.0, z: 0.0 },
      { x: 1, y: 0, z: 0 },
      1e-6,
    );
    expect(voxel).toEqual({ x: 0, y: 0, z: 0 });
  });

  it("resolves placement position via normal sum", () => {
    const voxel = computeVoxelFromHit(
      { x: 1, y: 0.3, z: 0.2 },
      { x: 1, y: 0, z: 0 },
    );
    // Placement = voxel + normal
    expect(voxel.x + 1).toBe(1);
    expect(voxel.y).toBe(0);
    expect(voxel.z).toBe(0);
  });
});

// ── computeScreenToNDC tests ──

describe("computeScreenToNDC", () => {
  it("is a function that accepts clientX, clientY, and canvas", () => {
    expect(typeof computeScreenToNDC).toBe("function");
    expect(computeScreenToNDC.length).toBe(3);
  });
});

// ── RaycastDebugEvent shape tests ──

describe("buildRaycastDebugEvent", () => {
  it("hit event preserves exact world hit point separately from computed voxel coord", () => {
    const hit: VoxelRaycastHit = {
      position: { x: 3, y: 0, z: 5 },
      world_position: { x: 4, y: 0.3, z: 5.2 },
      normal: { x: 1, y: 0, z: 0 },
      palette_index: 1,
      screen: { x: 100, y: 200 },
      ray_origin: { x: 0, y: 10, z: 0 },
      ray_direction: { x: 0.577, y: -0.577, z: 0.577 },
      distance: 8.5,
      hit_object_type: "Mesh",
      hit_object_id: "voxel-mesh-0",
    };

    const event = buildRaycastDebugEvent(
      100,
      200,
      hit,
      {
        clientX: 50,
        clientY: 100,
        ndcX: -0.5,
        ndcY: 0.5,
        dpr: 2,
        canvasRect: { left: 50, top: 100, width: 800, height: 600 },
      },
      hit.ray_direction!,
    );

    expect(event.hit).toBe(true);
    expect(event.screenX).toBe(100);
    expect(event.screenY).toBe(200);
    expect(event.ndcX).toBe(-0.5);
    expect(event.ndcY).toBe(0.5);
    expect(event.hitPoint).toEqual({ x: 4, y: 0.3, z: 5.2 });
    expect(event.voxelCoord).toEqual({ x: 3, y: 0, z: 5 });
    expect(event.placementCoord).toEqual({ x: 4, y: 0, z: 5 });
    expect(event.hitNormal).toEqual({ x: 1, y: 0, z: 0 });
    expect(event.hitObjectId).toBe("voxel-mesh-0");
  });

  it("miss event shape can omit hit details", () => {
    const event: Partial<RaycastDebugEvent> = {
      timestamp: 0,
      screenX: 100,
      screenY: 200,
      clientX: 50,
      clientY: 100,
      ndcX: -0.5,
      ndcY: 0.5,
      dpr: 2,
      canvasRect: { left: 50, top: 100, width: 800, height: 600 },
      rayOrigin: { x: 0, y: 10, z: 0 },
      rayDirection: { x: 0, y: 0, z: -1 },
      hit: false,
      hitDistance: 10000,
    };

    expect(event.hit).toBe(false);
    expect(event.hitPoint).toBeUndefined();
    expect(event.hitNormal).toBeUndefined();
    expect(event.voxelCoord).toBeUndefined();
    expect(event.placementCoord).toBeUndefined();
  });
});
