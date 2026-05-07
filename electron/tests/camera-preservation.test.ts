/**
 * Tests for camera preservation across mesh rebuilds.
 *
 * These tests validate the pure state-machine logic that decides
 * whether to frame the camera on mesh construction:
 *   - First mesh load → frame camera (initial framing)
 *   - Subsequent mesh rebuilds on same model → preserve user's orbit/pan/zoom
 *   - Model change (different model_id) → reset flag, frame new model
 *   - Explicit frameCurrentModel → always frame (user-initiated)
 *
 * Tests use the exported `shouldFrameCamera` pure function from scene.ts
 * to exercise the actual production decision logic directly.
 */

import { describe, it, expect } from "vitest";
import { shouldFrameCamera } from "../src/renderer/scene";

describe("camera framing decision logic (production function)", () => {
  it("frames camera on first mesh load (initialCameraFramed = false)", () => {
    expect(shouldFrameCamera(false, false)).toBe(true);
  });

  it("preserves camera on subsequent mesh rebuilds (initialCameraFramed = true)", () => {
    expect(shouldFrameCamera(true, false)).toBe(false);
  });

  it("frames camera on explicit frameCurrentModel() regardless of state", () => {
    // After initial frame
    expect(shouldFrameCamera(true, true)).toBe(true);
    // Before any frame
    expect(shouldFrameCamera(false, true)).toBe(true);
  });
});

/**
 * Simulate the full lifecycle of the camera framing flag as used in
 * VoxelForgeScene.buildMeshFromSnapshot and frameCurrentModel.
 */
describe("camera framing lifecycle", () => {
  it("frames on first build, not on subsequent rebuilds, frames on explicit request", () => {
    let initialCameraFramed = false;

    // First mesh build → should frame
    expect(shouldFrameCamera(initialCameraFramed, false)).toBe(true);
    initialCameraFramed = true;

    // Second mesh build (e.g., after voxel edit) → should NOT frame
    expect(shouldFrameCamera(initialCameraFramed, false)).toBe(false);

    // Third mesh build (e.g., after undo) → should NOT frame
    expect(shouldFrameCamera(initialCameraFramed, false)).toBe(false);

    // Explicit frameCurrentModel() → should always frame
    expect(shouldFrameCamera(initialCameraFramed, true)).toBe(true);
    // After explicit frame, the flag stays true → subsequent rebuilds still preserve camera
    expect(shouldFrameCamera(initialCameraFramed, false)).toBe(false);
  });

  it("handles explicit frame on first load gracefully (still frames)", () => {
    let initialCameraFramed = false;

    // frameCurrentModel() before first buildMeshFromSnapshot
    expect(shouldFrameCamera(initialCameraFramed, true)).toBe(true);
    // Flag not changed by explicit frame (it's controlled by buildMeshFromSnapshot only)

    // First mesh build → should frame
    expect(shouldFrameCamera(initialCameraFramed, false)).toBe(true);
    initialCameraFramed = true;

    // Subsequent → preserve
    expect(shouldFrameCamera(initialCameraFramed, false)).toBe(false);
  });

  /**
   * Model change tests: when model_id differs, the scene resets the flag to
   * false before calling shouldFrameCamera. These tests validate that after
   * the reset, a new model gets an initial auto-frame.
   */
  it("resets and frames when model changes (simulating first load of new model A)", () => {
    // After user interacted with old model, flag is true
    let initialCameraFramed = true;

    // Model changes → VoxelForgeScene resets flag to false
    initialCameraFramed = false;

    // First load of new model → should frame
    expect(shouldFrameCamera(initialCameraFramed, false)).toBe(true);
    initialCameraFramed = true;

    // Subsequent rebuilds on same model → preserve
    expect(shouldFrameCamera(initialCameraFramed, false)).toBe(false);
  });

  it("frames new model B after old model A was fully interacted with", () => {
    // Model A state
    let initialCameraFramed = false;
    // Load A → frame
    expect(shouldFrameCamera(initialCameraFramed, false)).toBe(true);
    initialCameraFramed = true;
    // Edit A → preserve
    expect(shouldFrameCamera(initialCameraFramed, false)).toBe(false);
    // Undo A → preserve
    expect(shouldFrameCamera(initialCameraFramed, false)).toBe(false);

    // Model changes to B → VoxelForgeScene resets flag
    initialCameraFramed = false;

    // Load B → frame
    expect(shouldFrameCamera(initialCameraFramed, false)).toBe(true);
    initialCameraFramed = true;
    // Edit B → preserve
    expect(shouldFrameCamera(initialCameraFramed, false)).toBe(false);
  });

  it("handles multiple sequential model loads correctly", () => {
    let initialCameraFramed = false;
    let lastModelId: string | null = null;

    function simulateBuildMesh(modelId: string, isExplicit = false) {
      // Model change detection (as in production scene.ts)
      if (modelId !== lastModelId) {
        initialCameraFramed = false;
        lastModelId = modelId;
      }

      const shouldFrame = shouldFrameCamera(initialCameraFramed, isExplicit);
      if (shouldFrame) {
        initialCameraFramed = true;
      }
      return shouldFrame;
    }

    // Load model A → frame
    expect(simulateBuildMesh("model-a")).toBe(true);
    // Edit model A → preserve
    expect(simulateBuildMesh("model-a")).toBe(false);
    // Undo model A → preserve
    expect(simulateBuildMesh("model-a")).toBe(false);

    // Load model B → frame (different from A)
    expect(simulateBuildMesh("model-b")).toBe(true);
    // Edit model B → preserve
    expect(simulateBuildMesh("model-b")).toBe(false);

    // Load model A again → frame (different from current B)
    expect(simulateBuildMesh("model-a")).toBe(true);
    // Edit model A → preserve
    expect(simulateBuildMesh("model-a")).toBe(false);

    // Explicit frame on model A
    expect(simulateBuildMesh("model-a", true)).toBe(true);
    // Still model A → preserve after explicit frame
    expect(simulateBuildMesh("model-a")).toBe(false);
  });
});

/**
 * Verify that VoxelForgeScene exposes the expected interface for camera
 * preservation. This is a structural/contract test that checks the class
 * definition contains the expected members.
 */
describe("VoxelForgeScene camera contract", () => {
  it("has frameCurrentModel method in the scene module", async () => {
    // Import the module to verify the export contract
    const sceneModule = await import("../src/renderer/scene");
    const VoxelForgeScene = sceneModule.VoxelForgeScene;

    // Verify the class has the expected methods for camera control
    const proto = VoxelForgeScene.prototype;

    // Must have frameCurrentModel for explicit user framing
    expect(typeof proto.frameCurrentModel).toBe("function");

    // Must have buildMeshFromSnapshot for mesh construction
    expect(typeof proto.buildMeshFromSnapshot).toBe("function");

    // Must have applyIncrementalUpdate for partial mesh updates
    expect(typeof proto.applyIncrementalUpdate).toBe("function");

    // Should NOT have any camera command or undo methods — camera is
    // purely TS-owned presentation state, not C#-backed.
    expect((proto as Record<string, unknown>).undoCamera).toBeUndefined();
    expect((proto as Record<string, unknown>).redoCamera).toBeUndefined();
    expect((proto as Record<string, unknown>).executeCameraCommand).toBeUndefined();
  });

  it("exports shouldFrameCamera pure function from the scene module", async () => {
    const sceneModule = await import("../src/renderer/scene");
    expect(typeof sceneModule.shouldFrameCamera).toBe("function");
  });
});
