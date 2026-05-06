import { describe, it, expect } from "vitest";
import { decodeByteArray } from "../src/shared/byte-utils";
import {
  cubeMeshSnapshot,
  emptyMeshSnapshot,
  base64ColorsCubeSnapshot,
  unitVoxelMeshSnapshot,
} from "./fixtures/mesh-data";

/**
 * These tests validate the pure data-transformation aspects of
 * scene construction — byte decoding, vertex/color processing,
 * index normalization — without requiring Three.js or WebGL.
 * They mirror the logic in VoxelForgeScene.buildMeshFromSnapshot
 * without the Three.js BufferGeometry/rendering dependencies.
 */

describe("mesh snapshot data integrity", () => {
  it("cube fixture has correct vertex/index counts", () => {
    expect(cubeMeshSnapshot.vertex_count).toBe(8);
    expect(cubeMeshSnapshot.index_count).toBe(36);
    expect(cubeMeshSnapshot.triangle_count).toBe(12);
  });

  it("cube fixture positions array has correct length", () => {
    // 8 vertices * 3 components = 24
    expect(cubeMeshSnapshot.positions.length).toBe(24);
  });

  it("cube fixture normals array has correct length", () => {
    // 8 vertices * 3 components = 24
    expect(cubeMeshSnapshot.normals.length).toBe(24);
  });

  it("cube fixture indices form complete triangles", () => {
    // Index count must be a multiple of 3
    expect(cubeMeshSnapshot.indices.length % 3).toBe(0);
  });

  it("cube fixture colors array has correct length", () => {
    // 8 vertices * 4 components (RGBA) = 32
    expect(cubeMeshSnapshot.colors.length).toBe(32);
  });

  it("empty fixture has zero counts and empty arrays", () => {
    expect(emptyMeshSnapshot.vertex_count).toBe(0);
    expect(emptyMeshSnapshot.index_count).toBe(0);
    expect(emptyMeshSnapshot.triangle_count).toBe(0);
    expect(emptyMeshSnapshot.positions).toEqual([]);
    expect(emptyMeshSnapshot.normals).toEqual([]);
    expect(emptyMeshSnapshot.colors).toEqual([]);
    expect(emptyMeshSnapshot.indices).toEqual([]);
  });
});

describe("vertex color processing", () => {
  it("decodes cube fixture colors as a number array", () => {
    // Two distinct palette indices: vertex 0-3 = gray (128,128,128,255), vertex 4-7 = red (255,0,0,255)
    const colors = decodeByteArray(cubeMeshSnapshot.colors, cubeMeshSnapshot.vertex_count * 4);
    expect(colors.length).toBe(32); // 8 vertices * 4

    // First vertex (palette 1 / gray)
    expect(colors[0]).toBe(128); // r
    expect(colors[1]).toBe(128); // g
    expect(colors[2]).toBe(128); // b
    expect(colors[3]).toBe(255); // a

    // Fifth vertex (palette 2 / red)
    expect(colors[16]).toBe(255); // r
    expect(colors[17]).toBe(0); // g
    expect(colors[18]).toBe(0); // b
    expect(colors[19]).toBe(255); // a
  });

  it("normalizes RGBA to RGB float [0..1] for Three.js", () => {
    const colors = decodeByteArray(cubeMeshSnapshot.colors, cubeMeshSnapshot.vertex_count * 4);
    const vertexCount = cubeMeshSnapshot.vertex_count;
    const rgbFloats = new Float32Array(vertexCount * 3);
    for (let i = 0; i < vertexCount; i++) {
      rgbFloats[i * 3] = colors[i * 4] / 255;
      rgbFloats[i * 3 + 1] = colors[i * 4 + 1] / 255;
      rgbFloats[i * 3 + 2] = colors[i * 4 + 2] / 255;
    }
    // First vertex: 128/255 ≈ 0.502
    expect(rgbFloats[0]).toBeCloseTo(128 / 255, 5);
    expect(rgbFloats[1]).toBeCloseTo(128 / 255, 5);
    expect(rgbFloats[2]).toBeCloseTo(128 / 255, 5);
    // Fifth vertex: red
    expect(rgbFloats[12]).toBeCloseTo(255 / 255, 5);
    expect(rgbFloats[13]).toBeCloseTo(0 / 255, 5);
    expect(rgbFloats[14]).toBeCloseTo(0 / 255, 5);
  });

  it("decodes base64 colors correctly", () => {
    const colors = decodeByteArray(
      base64ColorsCubeSnapshot.colors as string,
      base64ColorsCubeSnapshot.vertex_count * 4,
    );
    expect(colors.length).toBe(32);

    // Same values as the number-array fixture
    expect(colors[0]).toBe(128);
    expect(colors[1]).toBe(128);
    expect(colors[2]).toBe(128);
    expect(colors[3]).toBe(255);
    expect(colors[16]).toBe(255);
    expect(colors[17]).toBe(0);
    expect(colors[18]).toBe(0);
    expect(colors[19]).toBe(255);
  });
});

describe("position buffer processing", () => {
  it("creates a Float32Array from cube positions", () => {
    const positions = new Float32Array(cubeMeshSnapshot.positions);
    expect(positions.length).toBe(24);
    expect(positions[0]).toBe(-1);
    expect(positions[1]).toBe(-1);
    expect(positions[2]).toBe(1);
  });

  it("unit voxel bounds are within [-0.5, 0.5]", () => {
    const positions = new Float32Array(unitVoxelMeshSnapshot.positions);
    for (let i = 0; i < positions.length; i++) {
      expect(Math.abs(positions[i])).toBeLessThanOrEqual(0.5);
    }
  });
});

describe("index buffer processing", () => {
  it("all triangle indices are within vertex range", () => {
    const vertexCount = cubeMeshSnapshot.vertex_count;
    const indices = new Uint32Array(cubeMeshSnapshot.indices);
    for (let i = 0; i < indices.length; i++) {
      expect(indices[i]).toBeGreaterThanOrEqual(0);
      expect(indices[i]).toBeLessThan(vertexCount);
    }
  });

  it("triangle count matches index_count / 3", () => {
    expect(cubeMeshSnapshot.triangle_count).toBe(cubeMeshSnapshot.index_count / 3);
  });
});
