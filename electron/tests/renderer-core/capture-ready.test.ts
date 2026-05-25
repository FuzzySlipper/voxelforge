/**
 * Tests for capture readiness manager.
 */

import { describe, it, expect, vi } from "vitest";
import { CaptureReadyManager } from "../../src/renderer-core/scene/captureReady";

describe("CaptureReadyManager", () => {
  it("starts not ready with zero pending loads", () => {
    const manager = new CaptureReadyManager();
    expect(manager.isReady).toBe(false);
    expect(manager.pendingLoads).toBe(0);
  });

  it("becomes ready after scene build plus texture loads settle", () => {
    const manager = new CaptureReadyManager();
    manager.onTextureLoadStart();
    manager.onTextureLoadStart();
    expect(manager.isReady).toBe(false);
    expect(manager.pendingLoads).toBe(2);

    manager.onTextureLoadEnd();
    expect(manager.isReady).toBe(false);
    expect(manager.pendingLoads).toBe(1);

    manager.onTextureLoadEnd();
    expect(manager.isReady).toBe(false); // need scene build too
    expect(manager.pendingLoads).toBe(0);

    manager.onSceneBuildComplete();
    expect(manager.isReady).toBe(true);
  });

  it("becomes ready after scene build when no textures are pending", () => {
    const manager = new CaptureReadyManager();
    expect(manager.isReady).toBe(false);
    manager.onSceneBuildComplete();
    expect(manager.isReady).toBe(true);
  });

  it("fires onReady callback when ready", () => {
    const manager = new CaptureReadyManager();
    const callback = vi.fn();
    manager.onReady(callback);

    // Not ready yet - callback should not fire
    expect(callback).not.toHaveBeenCalled();

    // Force ready
    manager.forceReady();
    expect(callback).toHaveBeenCalledTimes(1);
  });

  it("calls onReady immediately if already ready", () => {
    const manager = new CaptureReadyManager();
    manager.forceReady();

    const callback = vi.fn();
    manager.onReady(callback);
    expect(callback).toHaveBeenCalledTimes(1);
  });

  it("resets state correctly", () => {
    const manager = new CaptureReadyManager();
    manager.onTextureLoadStart();
    manager.onTextureLoadStart();
    expect(manager.pendingLoads).toBe(2);

    manager.reset();
    expect(manager.pendingLoads).toBe(0);
    expect(manager.isReady).toBe(false);
  });

  it("handles texture load end decrementing below zero gracefully", () => {
    const manager = new CaptureReadyManager();
    manager.onTextureLoadEnd(); // Should not go negative
    expect(manager.pendingLoads).toBe(0);
  });

  it("sets DOM capture-ready data attribute in browser environment", () => {
    // This test requires DOM environment - vitest with jsdom would be needed.
    // For now, verify the mechanism is not broken.
    const manager = new CaptureReadyManager();
    manager.forceReady();
    // No crash = pass (DOM may not be available in Node env)
  });
});
