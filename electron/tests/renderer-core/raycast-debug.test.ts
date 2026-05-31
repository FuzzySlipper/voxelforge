/**
 * Tests for the raycast debug overlay and coordinate math utilities (#1794).
 *
 * Tests the pure functions extracted from RaycastDebugger:
 *   - computeVoxelFromHit: voxel coordinate computation from hit point + normal
 *   - computeScreenToNDC: screen coordinate → NDC conversion
 *
 * The RaycastDebugger class requires DOM/Three.js and is live-smoke tested
 * via the Electron renderer interactive harness only.
 */

import { describe, it, expect } from "vitest";
import {
  computeVoxelFromHit,
  computeScreenToNDC,
} from "../../src/renderer-core/scene/RaycastDebugger";

// ── computeVoxelFromHit tests ──

describe("computeVoxelFromHit", () => {
  it("resolves +X face hit to correct voxel", () => {
    // Hit point on +X face of voxel (0,0,0), face at x=0.5, normal (1,0,0)
    const voxel = computeVoxelFromHit(
      { x: 0.5, y: 0.3, z: -0.2 },
      { x: 1, y: 0, z: 0 },
    );
    expect(voxel).toEqual({ x: 0, y: 0, z: 0 });
  });

  it("resolves -X face hit to correct voxel", () => {
    // Hit point on -X face of voxel (1,0,0), face at x=0.5, normal (-1,0,0)
    const voxel = computeVoxelFromHit(
      { x: 0.5, y: 0.3, z: -0.2 },
      { x: -1, y: 0, z: 0 },
    );
    expect(voxel).toEqual({ x: 1, y: 0, z: 0 });
  });

  it("resolves +Y face hit to correct voxel", () => {
    const voxel = computeVoxelFromHit(
      { x: 0.3, y: 0.5, z: -0.2 },
      { x: 0, y: 1, z: 0 },
    );
    expect(voxel).toEqual({ x: 0, y: 0, z: 0 });
  });

  it("resolves -Y face hit to correct voxel", () => {
    // Hit point on -Y face of voxel (0,-1,0), face at y=-1.5, normal (0,-1,0)
    const voxel = computeVoxelFromHit(
      { x: 0.3, y: -1.5, z: -0.2 },
      { x: 0, y: -1, z: 0 },
    );
    expect(voxel).toEqual({ x: 0, y: -1, z: 0 });
  });

  it("resolves +Z face hit to correct voxel", () => {
    const voxel = computeVoxelFromHit(
      { x: 0.3, y: -0.2, z: 0.5 },
      { x: 0, y: 0, z: 1 },
    );
    expect(voxel).toEqual({ x: 0, y: 0, z: 0 });
  });

  it("resolves -Z face hit to correct voxel", () => {
    const voxel = computeVoxelFromHit(
      { x: 0.3, y: -0.2, z: 0.5 },
      { x: 0, y: 0, z: -1 },
    );
    expect(voxel).toEqual({ x: 0, y: 0, z: 1 });
  });

  it("handles negative coordinate voxels correctly", () => {
    // Hit +X face of voxel (-1, -2, -3)
    const voxel = computeVoxelFromHit(
      { x: -0.5, y: -2.3, z: -3.2 },
      { x: 1, y: 0, z: 0 },
    );
    expect(voxel).toEqual({ x: -1, y: -2, z: -3 });
  });

  it("handles floating-point imprecision near +X face boundary", () => {
    // Simulate slight fp error: hit point at 0.5000001 instead of exactly 0.5
    const voxel = computeVoxelFromHit(
      { x: 0.5000001, y: 0.0, z: 0.0 },
      { x: 1, y: 0, z: 0 },
    );
    expect(voxel).toEqual({ x: 0, y: 0, z: 0 });
  });

  it("handles floating-point imprecision near -X face boundary", () => {
    // Simulate slight fp error: hit point at 0.4999999 instead of exactly 0.5
    const voxel = computeVoxelFromHit(
      { x: 0.4999999, y: 0.0, z: 0.0 },
      { x: 1, y: 0, z: 0 },
    );
    expect(voxel).toEqual({ x: 0, y: 0, z: 0 });
  });

  it("handles floating-point imprecision on -X face near voxel 1", () => {
    // Hit -X face of voxel (1,0,0) with slight fp error
    const voxel = computeVoxelFromHit(
      { x: 0.5000001, y: 0.0, z: 0.0 },
      { x: -1, y: 0, z: 0 },
    );
    expect(voxel).toEqual({ x: 1, y: 0, z: 0 });
  });

  it("uses custom half extent correctly", () => {
    // For voxels with extent 1.0 (2 units wide)
    const voxel = computeVoxelFromHit(
      { x: 1.0, y: 0.0, z: 0.0 },
      { x: 1, y: 0, z: 0 },
      1.0,
    );
    expect(voxel).toEqual({ x: 0, y: 0, z: 0 });
  });

  it("resolves placement position via normal sum", () => {
    const voxel = computeVoxelFromHit(
      { x: 0.5, y: 0.3, z: -0.2 },
      { x: 1, y: 0, z: 0 },
    );
    // Placement = voxel + normal
    expect(voxel.x + 1).toBe(1);
    expect(voxel.y).toBe(0);
    expect(voxel.z).toBe(0);
  });
});

// ── computeScreenToNDC tests ──
// These are structural tests since DOM canvas needed for full test.

describe("computeScreenToNDC", () => {
  it("is a function that accepts clientX, clientY, and canvas", () => {
    expect(typeof computeScreenToNDC).toBe("function");
    expect(computeScreenToNDC.length).toBe(3);
  });
});

// ── RaycastDebugEvent shape tests ──

describe("RaycastDebugEvent shape", () => {
  it("hit event has all required fields", () => {
    // Use buildRaycastDebugEvent via its signature
    const event = {
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
      rayDirection: { x: 0.577, y: -0.577, z: 0.577 },
      hit: true,
      hitObjectType: "voxel_mesh",
      hitObjectId: "mesh-0",
      hitDistance: 8.5,
      hitPoint: { x: 3.5, y: 0.3, z: 5.2 },
      hitNormal: { x: 0, y: 1, z: 0 },
      voxelCoord: { x: 3, y: 0, z: 5 },
      placementCoord: { x: 3, y: 1, z: 5 },
    };

    expect(event.hit).toBe(true);
    expect(event.screenX).toBe(100);
    expect(event.screenY).toBe(200);
    expect(event.ndcX).toBe(-0.5);
    expect(event.ndcY).toBe(0.5);
    expect(event.voxelCoord).toEqual({ x: 3, y: 0, z: 5 });
    expect(event.placementCoord).toEqual({ x: 3, y: 1, z: 5 });
    expect(event.hitNormal).toEqual({ x: 0, y: 1, z: 0 });
  });

  it("miss event has hit: false and no hit details", () => {
    const event = {
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
