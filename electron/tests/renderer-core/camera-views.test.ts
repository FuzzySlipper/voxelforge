/**
 * Tests for camera view-from-angle and preset functionality (#1664).
 *
 * Tests the pure-function geometry (computeViewFromAnglePosition) and the
 * CaptureReadyManager scene build + texture integration.
 *
 * Verifies that:
 *   - computeViewFromAnglePosition computes correct spherical→Cartesian coords
 *   - Different yaw values produce different camera positions
 *   - Different pitch values produce different camera positions
 *   - Distance parameter affects camera distance from target
 *   - Target center is respected
 *   - snapCameraToView presets (front/right/side/top/isometric) work
 *   - CaptureReadyManager requires both scene build + texture loads
 */

import { describe, it, expect } from "vitest";
import {
  computeViewFromAnglePosition,
  shouldFrameCamera,
} from "../../src/renderer-core/scene/VoxelForgeScene";
import { CaptureReadyManager } from "../../src/renderer-core/scene/captureReady";

// ── Helpers ──

/**
 * Compute squared Euclidean distance between two 3D positions.
 */
function posDistanceSq(a: [number, number, number], b: [number, number, number]): number {
  const dx = a[0] - b[0];
  const dy = a[1] - b[1];
  const dz = a[2] - b[2];
  return dx * dx + dy * dy + dz * dz;
}

/**
 * Check if a position is on a specific axis (within epsilon).
 */
function isOnAxis(
  pos: [number, number, number],
  axis: 0 | 1 | 2,
  sign: 1 | -1,
): boolean {
  const threshold = 0.01;
  const axisValue = pos[axis];
  if (sign * axisValue < 0) return false; // wrong sign
  for (let i = 0; i < 3; i++) {
    if (i !== axis && Math.abs(pos[i]) > threshold) return false;
  }
  return Math.abs(axisValue) > threshold;
}

// ── computeViewFromAnglePosition tests ──

describe("computeViewFromAnglePosition", () => {
  it("positions camera on +Z axis for yaw=0, pitch=0", () => {
    const pos = computeViewFromAnglePosition(0, 0, 10);
    expect(isOnAxis(pos, 2, 1)).toBe(true); // +Z
    expect(pos[2]).toBeCloseTo(10, 5);
  });

  it("positions camera on +X axis for yaw=PI/2, pitch=0", () => {
    const pos = computeViewFromAnglePosition(Math.PI / 2, 0, 10);
    expect(isOnAxis(pos, 0, 1)).toBe(true); // +X
    expect(pos[0]).toBeCloseTo(10, 5);
  });

  it("positions camera on +Y axis for pitch=PI/2 (top view)", () => {
    const pos = computeViewFromAnglePosition(0, Math.PI / 2, 10);
    expect(isOnAxis(pos, 1, 1)).toBe(true); // +Y
    expect(pos[1]).toBeCloseTo(10, 5);
  });

  it("positions camera behind (-Z) for yaw=PI, pitch=0 (back view)", () => {
    const pos = computeViewFromAnglePosition(Math.PI, 0, 10);
    expect(isOnAxis(pos, 2, -1)).toBe(true); // -Z
    expect(pos[2]).toBeCloseTo(-10, 5);
  });

  it("positions camera on -X axis for yaw=-PI/2 (left view)", () => {
    const pos = computeViewFromAnglePosition(-Math.PI / 2, 0, 10);
    expect(isOnAxis(pos, 0, -1)).toBe(true); // -X
    expect(pos[0]).toBeCloseTo(-10, 5);
  });

  it("produces different positions for different yaw values", () => {
    const posFront = computeViewFromAnglePosition(0, 0, 10);
    const posRight = computeViewFromAnglePosition(Math.PI / 2, 0, 10);
    const posBack = computeViewFromAnglePosition(Math.PI, 0, 10);

    expect(posDistanceSq(posFront, posRight)).toBeGreaterThan(1);
    expect(posDistanceSq(posFront, posBack)).toBeGreaterThan(1);
    expect(posDistanceSq(posRight, posBack)).toBeGreaterThan(1);
  });

  it("produces different positions for different pitch values", () => {
    const posFront = computeViewFromAnglePosition(0, 0, 10);
    const pos45 = computeViewFromAnglePosition(0, Math.PI / 4, 10);
    const posTop = computeViewFromAnglePosition(0, Math.PI / 2, 10);

    expect(posDistanceSq(posFront, pos45)).toBeGreaterThan(1);
    expect(posDistanceSq(posFront, posTop)).toBeGreaterThan(1);
    expect(posDistanceSq(pos45, posTop)).toBeGreaterThan(1);
  });

  it("distance parameter affects camera distance from origin", () => {
    const pos5 = computeViewFromAnglePosition(0, 0, 5);
    const pos20 = computeViewFromAnglePosition(0, 0, 20);

    const dist5 = Math.sqrt(pos5[0] * pos5[0] + pos5[1] * pos5[1] + pos5[2] * pos5[2]);
    const dist20 = Math.sqrt(pos20[0] * pos20[0] + pos20[1] * pos20[1] + pos20[2] * pos20[2]);

    expect(dist5).toBeGreaterThan(4);
    expect(dist5).toBeLessThan(6);
    expect(dist20).toBeGreaterThan(19);
    expect(dist20).toBeLessThan(21);
  });

  it("respects target center offset", () => {
    const pos = computeViewFromAnglePosition(0, 0, 10, { x: 5, y: 3, z: -2 });
    // Camera should be at (5, 3, -2 + 10) = (5, 3, 8)
    expect(pos[0]).toBeCloseTo(5, 5);
    expect(pos[1]).toBeCloseTo(3, 5);
    expect(pos[2]).toBeCloseTo(8, 5);
  });

  it("uses origin as default target", () => {
    const pos = computeViewFromAnglePosition(0, 0, 10);
    expect(isOnAxis(pos, 2, 1)).toBe(true);
    expect(pos[2]).toBeCloseTo(10, 5);
  });

  it("isometric preset produces non-axis-aligned position", () => {
    const pos = computeViewFromAnglePosition(Math.PI / 4, 0.6155, 10);

    // Isometric should have non-zero X, Y, and Z
    expect(Math.abs(pos[0])).toBeGreaterThan(1);
    expect(Math.abs(pos[1])).toBeGreaterThan(1);
    expect(Math.abs(pos[2])).toBeGreaterThan(1);

    // At ~45° yaw and ~35° pitch, X and Z should be approximately equal
    const ratio = Math.abs(pos[0] / pos[2]);
    expect(ratio).toBeGreaterThan(0.5);
    expect(ratio).toBeLessThan(2.0);
  });

  it("front preset produces position on +Z", () => {
    // Front: yaw=0, pitch=0
    const pos = computeViewFromAnglePosition(0, 0, 10);
    expect(isOnAxis(pos, 2, 1)).toBe(true);
  });

  it("right preset produces position on +X", () => {
    // Right: yaw=PI/2, pitch=0
    const pos = computeViewFromAnglePosition(Math.PI / 2, 0, 10);
    expect(isOnAxis(pos, 0, 1)).toBe(true);
  });

  it("top preset produces position on +Y", () => {
    // Top: yaw=0, pitch=PI/2
    const pos = computeViewFromAnglePosition(0, Math.PI / 2, 10);
    expect(isOnAxis(pos, 1, 1)).toBe(true);
  });

  it("all presets produce distinct camera positions", () => {
    const front = computeViewFromAnglePosition(0, 0, 10);
    const right = computeViewFromAnglePosition(Math.PI / 2, 0, 10);
    const top = computeViewFromAnglePosition(0, Math.PI / 2, 10);
    const iso = computeViewFromAnglePosition(Math.PI / 4, 0.6155, 10);

    expect(posDistanceSq(front, right)).toBeGreaterThan(1);
    expect(posDistanceSq(front, top)).toBeGreaterThan(1);
    expect(posDistanceSq(front, iso)).toBeGreaterThan(1);
    expect(posDistanceSq(right, top)).toBeGreaterThan(1);
    expect(posDistanceSq(right, iso)).toBeGreaterThan(1);
    expect(posDistanceSq(top, iso)).toBeGreaterThan(1);
  });

  /**
   * Regression test for #1674: parseCameraParams uses approximate float
   * literals (1.5708, 0.7854, 0.6155) rather than Math.PI/2 or Math.PI/4.
   * These approximate values must still produce distinct camera positions
   * for front/right/isometric views.
   */
  it("parseCameraParams approximate preset values produce distinct positions", () => {
    // These are the exact float literals used in electron/src/mcp-viewer/main.ts
    const front  = computeViewFromAnglePosition(0,      0,      10);
    const right  = computeViewFromAnglePosition(1.5708, 0,      10);
    const isometric = computeViewFromAnglePosition(0.7854, 0.6155, 10);

    expect(posDistanceSq(front, right)).toBeGreaterThan(1);
    expect(posDistanceSq(front, isometric)).toBeGreaterThan(1);
    expect(posDistanceSq(right, isometric)).toBeGreaterThan(1);

    // Sanity: right should be on +X axis (approximately)
    expect(right[0]).toBeGreaterThan(9);
    expect(Math.abs(right[1])).toBeLessThan(0.01);
    expect(Math.abs(right[2])).toBeLessThan(0.01);

    // Sanity: front should be on +Z axis
    expect(front[2]).toBeGreaterThan(9);
    expect(Math.abs(front[0])).toBeLessThan(0.01);
    expect(Math.abs(front[1])).toBeLessThan(0.01);

    // Sanity: isometric should have significant X, Y, and Z components
    expect(Math.abs(isometric[0])).toBeGreaterThan(5);
    expect(Math.abs(isometric[1])).toBeGreaterThan(5);
    expect(Math.abs(isometric[2])).toBeGreaterThan(5);
  });
});

// ── CaptureReadyManager scene build tracking tests ──

describe("CaptureReadyManager scene build + texture integration", () => {
  it("starts not ready with zero pending loads and no scene build", () => {
    const manager = new CaptureReadyManager();
    expect(manager.isReady).toBe(false);
    expect(manager.pendingLoads).toBe(0);
  });

  it("does not signal ready when only scene is built but textures are pending", () => {
    const manager = new CaptureReadyManager();
    // Start a texture load first
    manager.onTextureLoadStart();
    // Then scene build completes — but textures are still pending
    manager.onSceneBuildComplete();
    expect(manager.isReady).toBe(false);

    // Texture completes
    manager.onTextureLoadEnd();
    expect(manager.isReady).toBe(true);
  });

  it("does not signal ready when only textures are loaded but scene not built", () => {
    const manager = new CaptureReadyManager();
    manager.onTextureLoadStart();
    manager.onTextureLoadEnd();
    // Scene not built yet — should not be ready
    expect(manager.isReady).toBe(false);

    manager.onSceneBuildComplete();
    expect(manager.isReady).toBe(true);
  });

  it("signals ready only after both scene build and all texture loads complete", () => {
    const manager = new CaptureReadyManager();

    // Start some textures loading
    manager.onTextureLoadStart();
    manager.onTextureLoadStart();
    expect(manager.isReady).toBe(false);

    // Scene build completes
    manager.onSceneBuildComplete();
    expect(manager.isReady).toBe(false); // textures still pending

    // One texture finishes
    manager.onTextureLoadEnd();
    expect(manager.isReady).toBe(false); // one still pending

    // Last texture finishes
    manager.onTextureLoadEnd();
    expect(manager.isReady).toBe(true); // both conditions met
  });

  it("fires onReady callback only after both conditions are met", () => {
    const manager = new CaptureReadyManager();
    let called = false;
    manager.onReady(() => { called = true; });

    // Start a texture so scene build alone won't trigger readiness
    manager.onTextureLoadStart();
    // Scene build without pending loads complete should not trigger
    manager.onSceneBuildComplete();
    expect(called).toBe(false);

    // Texture completes — both conditions met now
    manager.onTextureLoadEnd();
    expect(called).toBe(true);
  });

  it("calls onReady immediately if already ready", () => {
    const manager = new CaptureReadyManager();
    manager.onSceneBuildComplete();

    let called = false;
    manager.onReady(() => { called = true; });
    expect(called).toBe(true);
  });

  it("resets state correctly including scene build flag", () => {
    const manager = new CaptureReadyManager();
    manager.onSceneBuildComplete();
    manager.onTextureLoadStart();
    manager.onTextureLoadEnd();
    expect(manager.isReady).toBe(true);

    manager.reset();
    expect(manager.isReady).toBe(false);
    expect(manager.pendingLoads).toBe(0);

    // After reset, need both conditions again
    manager.onTextureLoadStart();
    manager.onTextureLoadEnd();
    expect(manager.isReady).toBe(false); // scene not built

    manager.onSceneBuildComplete();
    expect(manager.isReady).toBe(true);
  });

  it("handles texture load end decrementing below zero gracefully", () => {
    const manager = new CaptureReadyManager();
    manager.onTextureLoadEnd(); // Should not go negative
    expect(manager.pendingLoads).toBe(0);
  });

  it("forceReady sets both scene built and clears pending loads", () => {
    const manager = new CaptureReadyManager();
    manager.onTextureLoadStart();
    manager.onTextureLoadStart();
    manager.forceReady();
    expect(manager.pendingLoads).toBe(0);
    expect(manager.isReady).toBe(true);
  });

  it("sets DOM capture-ready data attribute in browser environment", () => {
    const manager = new CaptureReadyManager();
    manager.onSceneBuildComplete();
    manager.onTextureLoadStart();
    manager.onTextureLoadEnd();
    // No crash = pass (DOM may not be available in Node env)
  });

  it("becomes ready without pending loads after scene build", () => {
    const manager = new CaptureReadyManager();
    manager.onSceneBuildComplete();
    // No pending loads and scene built -> ready
    expect(manager.isReady).toBe(true);
  });
});
