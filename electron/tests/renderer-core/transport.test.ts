/**
 * Tests for transport clients: HttpSseRenderClient, DenBridgeRenderClient.
 * Uses mocked fetch/EventSource for browser transport.
 */

import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

// ── HttpSseRenderClient tests ──

describe("HttpSseRenderClient", () => {
  let fetchMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("can be instantiated without options", async () => {
    // Dynamic import so the fetch mock is in place
    const { HttpSseRenderClient } = await import(
      "../../src/renderer-core/transport/HttpSseRenderClient"
    );
    const client = new HttpSseRenderClient();
    expect(client).toBeDefined();
    expect(client.isConnected()).toBe(false);
  });

  it("returns empty snapshot with connected=false on fetch failure", async () => {
    fetchMock.mockRejectedValue(new Error("Network error"));

    const { HttpSseRenderClient } = await import(
      "../../src/renderer-core/transport/HttpSseRenderClient"
    );
    const client = new HttpSseRenderClient({ baseUrl: "http://test:9999" });
    const snapshot = await client.getRenderSnapshot();

    // Falls back to transitional, which also fails, returns empty snapshot
    expect(snapshot.model_id).toBe("default");
    expect(snapshot.voxel_meshes).toHaveLength(0);
  });

  it("fetches render snapshot via /api/render/snapshot", async () => {
    const mockSnapshot = {
      schema_version: "voxelforge.render_scene@1",
      revision: 42,
      model_id: "test",
      source: { host: "mcp", capabilities: [] },
      bounds: null,
      reference_bounds: null,
      combined_bounds: null,
      voxel_meshes: [],
      reference_nodes: [],
      materials: [],
      textures: [],
      palette: [],
      diagnostics: [],
    };

    fetchMock.mockResolvedValue({
      ok: true,
      json: () => Promise.resolve(mockSnapshot),
    });

    const { HttpSseRenderClient } = await import(
      "../../src/renderer-core/transport/HttpSseRenderClient"
    );
    const client = new HttpSseRenderClient({ baseUrl: "http://test:9999" });
    const snapshot = await client.getRenderSnapshot();

    expect(snapshot.model_id).toBe("test");
    expect(snapshot.revision).toBe(42);
    expect(fetchMock).toHaveBeenCalledWith(
      "http://test:9999/api/render/snapshot",
    );
  });

  it("falls back to transitional endpoints when /api/render/snapshot fails", async () => {
    // First call (/api/render/snapshot) fails
    fetchMock
      .mockResolvedValueOnce({ ok: false, status: 404 })
      // Transitional state succeeds
      .mockResolvedValueOnce({
        ok: true,
        json: () =>
          Promise.resolve({
            model_name: "fallback-model",
            voxel_count: 50,
            revision: 5,
            grid_hint: 16,
            reference_model_count: 0,
            reference_vertex_count: 0,
            palette_entries: [],
            bounds: null,
          }),
      })
      // Transitional mesh succeeds
      .mockResolvedValueOnce({
        ok: true,
        json: () =>
          Promise.resolve({
            model_id: "fallback-model",
            mesh_id: "mesh-1",
            format: "json",
            vertex_count: 0,
            index_count: 0,
            triangle_count: 0,
            positions: [],
            normals: [],
            colors: [],
            indices: [],
          }),
      });

    const { HttpSseRenderClient } = await import(
      "../../src/renderer-core/transport/HttpSseRenderClient"
    );
    const client = new HttpSseRenderClient({ baseUrl: "http://test:9999" });
    const snapshot = await client.getRenderSnapshot();

    expect(snapshot.model_id).toBe("fallback-model");
    expect(snapshot.source.host).toBe("mcp");
    // Should have called /api/render/snapshot first, then transitional endpoints
    expect(fetchMock).toHaveBeenCalledTimes(3);
  });
});

// ── DenBridgeRenderClient tests ──

describe("DenBridgeRenderClient", () => {
  beforeEach(() => {
    // Reset the global window mock
    delete (globalThis as any).window;
  });

  it("returns disconnected state when no bridge available", async () => {
    const { DenBridgeRenderClient } = await import(
      "../../src/renderer-core/transport/DenBridgeRenderClient"
    );
    const client = new DenBridgeRenderClient();
    const state = await client.getRenderState();
    expect(state.connected).toBe(false);
  });

  it("returns empty snapshot when no bridge available", async () => {
    const { DenBridgeRenderClient } = await import(
      "../../src/renderer-core/transport/DenBridgeRenderClient"
    );
    const client = new DenBridgeRenderClient();
    const snapshot = await client.getRenderSnapshot();
    expect(snapshot.schema_version).toBe("voxelforge.render_scene@1");
    expect(snapshot.source.host).toBe("bridge");
    expect(snapshot.voxel_meshes).toHaveLength(0);
  });

  it("returns empty unsubscribe when no bridge available", () => {
    // Just verify subscribeEvents doesn't crash
    const { DenBridgeRenderClient } = (globalThis as any).__denBridgeClient ?? {};
    // Dynamic import
    import("../../src/renderer-core/transport/DenBridgeRenderClient").then(
      ({ DenBridgeRenderClient }) => {
        const client = new DenBridgeRenderClient();
        const unsub = client.subscribeEvents(() => {});
        expect(typeof unsub).toBe("function");
        unsub();
      },
    );
  });
});
