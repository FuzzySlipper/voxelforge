/**
 * HTTP + SSE render client for browser (MCP viewer) usage.
 * Uses fetch for /api/render/state, /api/render/snapshot and
 * EventSource for /api/viewer-events SSE subscription.
 * Falls back to transitional /api/viewer-state, /api/mesh-snapshot endpoints.
 */

import type {
  RenderProtocolClient,
} from "./RenderProtocolClient";
import type {
  RenderSceneSnapshot,
  RenderStateSummary,
  ViewerSseEvent,
  TransitionalViewerState,
  TransitionalMeshSnapshot,
} from "../protocol/types";
import {
  normalizeSnapshot,
  transitionalStateToSummary,
  transitionalMeshToSnapshot,
  snapshotToStateSummary,
} from "../protocol/normalizeSnapshot";

export interface HttpSseRenderClientOptions {
  /** Base URL for API requests (default: window.location.origin) */
  baseUrl?: string;
  /** Use transitional API endpoints (/api/viewer-state, /api/mesh-snapshot) */
  preferTransitional?: boolean;
  /** Poll interval in ms when SSE is unavailable (default: 2000) */
  pollIntervalMs?: number;
  /** Enable capture mode (no SSE, no animation loop) */
  captureMode?: boolean;
}

export class HttpSseRenderClient implements RenderProtocolClient {
  private baseUrl: string;
  private preferTransitional: boolean;
  private pollIntervalMs: number;
  private captureMode: boolean;
  private sseSource: EventSource | null = null;
  private sseConnected = false;
  private connected = false;
  private lastRevision = -1;
  private _lastStateSummary: RenderStateSummary | null = null;
  private abortController: AbortController | null = null;

  constructor(options: HttpSseRenderClientOptions = {}) {
    this.baseUrl = options.baseUrl ?? (
      typeof window !== "undefined"
        ? window.location.origin
        : "http://localhost:5100"
    );
    this.preferTransitional = options.preferTransitional ?? false;
    this.pollIntervalMs = options.pollIntervalMs ?? 2000;
    this.captureMode = options.captureMode ??
      (typeof URL !== "undefined" &&
        new URLSearchParams(
          typeof window !== "undefined"
            ? window.location.search
            : "",
        ).get("capture") === "1");
  }

  async getRenderState(): Promise<RenderStateSummary> {
    if (!this.preferTransitional) {
      try {
        const snapshot = await this.getRenderSnapshot();
        const summary = snapshotToStateSummary(snapshot);
        this._lastStateSummary = summary;
        this.connected = true;
        return summary;
      } catch {
        // Fall through to transitional endpoint
      }
    }

    // Fallback to transitional /api/viewer-state
    try {
      const resp = await fetch(`${this.baseUrl}/api/viewer-state`);
      if (!resp.ok) throw new Error(`HTTP ${resp.status}`);
      const state = (await resp.json()) as TransitionalViewerState;
      const summary = transitionalStateToSummary(state);
      this._lastStateSummary = summary;
      this.connected = true;
      return summary;
    } catch (err) {
      this.connected = false;
      return {
        connected: false,
        model_name: "\u2014",
        voxel_count: 0,
        revision: 0,
        reference_model_count: 0,
        reference_vertex_count: 0,
        capture_ready: false,
        pending_texture_loads: 0,
        ...(err instanceof Error ? {} : {}),
      };
    }
  }

  async getRenderSnapshot(): Promise<RenderSceneSnapshot> {
    if (!this.preferTransitional) {
      try {
        const resp = await fetch(
          `${this.baseUrl}/api/render/snapshot`,
        );
        if (resp.ok) {
          const data = (await resp.json()) as RenderSceneSnapshot;
          return normalizeSnapshot(data);
        }
      } catch {
        // Fall through to transitional
      }
    }

    // Fallback: get transitional state + mesh and convert
    const [state, mesh] = await Promise.all([
      this.fetchTransitionalState(),
      this.fetchTransitionalMesh(),
    ]);

    if (mesh) {
      this.connected = true;
      return transitionalMeshToSnapshot(mesh, state);
    }

    // Return empty snapshot
    return {
      schema_version: "voxelforge.render_scene@1",
      revision: state?.revision ?? 0,
      model_id: state?.model_name ?? "default",
      source: { host: "mcp", capabilities: ["voxel_mesh"] },
      bounds: state?.bounds ?? null,
      reference_bounds: null,
      combined_bounds: null,
      voxel_meshes: [],
      reference_nodes: [],
      materials: [],
      textures: [],
      palette: [],
      diagnostics: [],
    };
  }

  subscribeEvents(
    onEvent: (event: ViewerSseEvent) => void,
    onError?: (error: unknown) => void,
  ): () => void {
    if (this.captureMode) {
      // No SSE in capture mode
      return () => {};
    }

    this.disconnectSse();

    try {
      this.sseSource = new EventSource(
        `${this.baseUrl}/api/viewer-events`,
      );
      this.sseSource.onopen = () => {
        this.sseConnected = true;
      };
      this.sseSource.onmessage = (event: MessageEvent) => {
        try {
          const data = JSON.parse(event.data) as ViewerSseEvent;
          onEvent(data);
          if (
            data.type === "connected" ||
            data.type === "revision"
          ) {
            this.lastRevision = data.revision;
            this.connected = true;
          }
        } catch (e) {
          onError?.(e);
        }
      };
      this.sseSource.onerror = () => {
        this.sseConnected = false;
        this.disconnectSse();
        onError?.(new Error("SSE connection lost"));
      };
    } catch (err) {
      onError?.(err);
    }

    return () => this.disconnectSse();
  }

  isConnected(): boolean {
    return this.connected;
  }

  /** Access the last fetched state summary. */
  get lastStateSummary(): RenderStateSummary | null {
    return this._lastStateSummary;
  }

  /** Access the last known revision. */
  get lastRevisionValue(): number {
    return this.lastRevision;
  }

  /** Whether SSE is actively connected. */
  get isSseConnected(): boolean {
    return this.sseConnected;
  }

  /** Clean up SSE and abort pending requests. */
  dispose(): void {
    this.disconnectSse();
    this.abortController?.abort();
  }

  // ── Private ──

  private disconnectSse(): void {
    if (this.sseSource) {
      this.sseSource.close();
      this.sseSource = null;
    }
    this.sseConnected = false;
  }

  private async fetchTransitionalState(): Promise<TransitionalViewerState | null> {
    try {
      const resp = await fetch(`${this.baseUrl}/api/viewer-state`);
      if (!resp.ok) return null;
      return (await resp.json()) as TransitionalViewerState;
    } catch {
      return null;
    }
  }

  private async fetchTransitionalMesh(): Promise<TransitionalMeshSnapshot | null> {
    try {
      const resp = await fetch(`${this.baseUrl}/api/mesh-snapshot`);
      if (!resp.ok) return null;
      return (await resp.json()) as TransitionalMeshSnapshot;
    } catch {
      return null;
    }
  }
}
