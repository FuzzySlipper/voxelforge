import { describe, it, expect } from "vitest";
import { computePlacementPosition, type VoxelRaycastHit } from "../src/shared/compute-placement";

function makeHit(overrides: Partial<VoxelRaycastHit> = {}): VoxelRaycastHit {
  return {
    position: { x: 0, y: 0, z: 0 },
    normal: { x: 0, y: 1, z: 0 },
    palette_index: 1,
    screen: { x: 400, y: 300 },
    ray_origin: { x: 0, y: 10, z: 0 },
    distance: 10,
    ...overrides,
  };
}

describe("computePlacementPosition", () => {
  it("places above when hit normal is +Y", () => {
    const hit = makeHit({ position: { x: 5, y: 3, z: 7 }, normal: { x: 0, y: 1, z: 0 } });
    const result = computePlacementPosition(hit);
    expect(result).toEqual({ x: 5, y: 4, z: 7 });
  });

  it("places below when hit normal is -Y", () => {
    const hit = makeHit({ position: { x: 5, y: 3, z: 7 }, normal: { x: 0, y: -1, z: 0 } });
    const result = computePlacementPosition(hit);
    expect(result).toEqual({ x: 5, y: 2, z: 7 });
  });

  it("places to the right when hit normal is +X", () => {
    const hit = makeHit({ position: { x: 2, y: 0, z: 0 }, normal: { x: 1, y: 0, z: 0 } });
    const result = computePlacementPosition(hit);
    expect(result).toEqual({ x: 3, y: 0, z: 0 });
  });

  it("places to the left when hit normal is -X", () => {
    const hit = makeHit({ position: { x: 2, y: 0, z: 0 }, normal: { x: -1, y: 0, z: 0 } });
    const result = computePlacementPosition(hit);
    expect(result).toEqual({ x: 1, y: 0, z: 0 });
  });

  it("places forward when hit normal is +Z", () => {
    const hit = makeHit({ position: { x: 0, y: 0, z: 3 }, normal: { x: 0, y: 0, z: 1 } });
    const result = computePlacementPosition(hit);
    expect(result).toEqual({ x: 0, y: 0, z: 4 });
  });

  it("places backward when hit normal is -Z", () => {
    const hit = makeHit({ position: { x: 0, y: 0, z: 3 }, normal: { x: 0, y: 0, z: -1 } });
    const result = computePlacementPosition(hit);
    expect(result).toEqual({ x: 0, y: 0, z: 2 });
  });

  it("works with negative hit position coordinates", () => {
    const hit = makeHit({ position: { x: -3, y: -5, z: -2 }, normal: { x: 1, y: 0, z: 0 } });
    const result = computePlacementPosition(hit);
    expect(result).toEqual({ x: -2, y: -5, z: -2 });
  });

  it("works with negative normals on negative positions", () => {
    const hit = makeHit({ position: { x: -3, y: -5, z: -2 }, normal: { x: 0, y: 0, z: -1 } });
    const result = computePlacementPosition(hit);
    expect(result).toEqual({ x: -3, y: -5, z: -3 });
  });

  it("handles diagonal normal (not axis-aligned)", () => {
    // The real raycast rounds normals to axis, but test that the
    // pure function still works with arbitrary normals.
    const hit = makeHit({ position: { x: 0, y: 0, z: 0 }, normal: { x: 1, y: 1, z: 0 } });
    const result = computePlacementPosition(hit);
    expect(result).toEqual({ x: 1, y: 1, z: 0 });
  });
});
