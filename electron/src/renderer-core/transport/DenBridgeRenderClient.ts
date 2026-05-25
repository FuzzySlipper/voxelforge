/**
 * DenBridgeRenderClient — Electron renderer transport using the preload bridge.
 * Maps render protocol operations to voxelforgeBridge IPC request/event channels.
 *
 * This client runs inside the Electron renderer process and communicates
 * through the preload bridge to the main process, which relays to the C# sidecar.
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
  RenderCommand,
} from "../protocol/types";
import {
  normalizeSnapshot,
  transitionalStateToSummary,
  transitionalMeshToSnapshot,
  snapshotToStateSummary,
} from "../protocol/normalizeSnapshot";
import type { BridgeRequestResponse } from "./RenderProtocolClient";

/**
 * Shape of the Electron preload bridge exposed on window.voxelforgeBridge.
 */
export interface VoxelForgeBridge {
  request(channel: string, payload: unknown): Promise<unknown>;
  onEvent(channel: string, callback: (payload: unknown) => void): () => void;
  notifyReady(): void;
  sendMetrics(metrics: Record<string, number>): void;
}

/**
 * Get the bridge from window, returning null if unavailable.
 */
/** Get the voxelforgeBridge from the Window object, if available. */
function getBridge(): VoxelForgeBridge | null {
  if (typeof window === "undefined") return null;
  const w = window as { voxelforgeBridge?: VoxelForgeBridge };
  return w.voxelforgeBridge ?? null;
}

export interface DenBridgeRenderClientOptions {
  /** Bridge channel name for render snapshot requests (default: "bridge:render-snapshot") */
  snapshotChannel?: string;
  /** Bridge channel name for render state requests (default: "bridge:render-state") */
  stateChannel?: string;
}

export class DenBridgeRenderClient implements RenderProtocolClient {
  private options: DenBridgeRenderClientOptions;
  private _connected = false;
  private _lastRevision = -1;
  private _lastStateSummary: RenderStateSummary | null = null;
  private unsubscribeFns: (() => void)[] = [];

  constructor(options: DenBridgeRenderClientOptions = {}) {
    this.options = {
      snapshotChannel: "bridge:render-snapshot",
      stateChannel: "bridge:render-state",
      ...options,
    };
  }

  async getRenderState(): Promise<RenderStateSummary> {
    const bridge = getBridge();
    if (!bridge) {
      return {
        connected: false,
        model_name: "\u2014",
        voxel_count: 0,
        revision: 0,
        reference_model_count: 0,
        reference_vertex_count: 0,
        capture_ready: false,
        pending_texture_loads: 0,
      };
    }

    try {
      const response = await bridge.request(
        this.options.stateChannel!,
        {},
      ) as BridgeRequestResponse<TransitionalViewerState | RenderStateSummary>;

      if ((response as { success?: boolean }).success === false) {
        throw new Error(
          (response as { error?: string }).error ?? "Bridge request failed",
        );
      }

      const data = (response as { data?: unknown }).data ?? response;

      // Determine if this is a transitional state or a direct summary
      if ((data as RenderStateSummary).capture_ready !== undefined) {
        this._lastStateSummary = data as RenderStateSummary;
      } else {
        this._lastStateSummary = transitionalStateToSummary(
          data as TransitionalViewerState,
        );
      }

      this._connected = true;
      return this._lastStateSummary;
    } catch (err) {
      this._connected = false;
      return {
        connected: false,
        model_name: "\u2014",
        voxel_count: 0,
        revision: 0,
        reference_model_count: 0,
        reference_vertex_count: 0,
        capture_ready: false,
        pending_texture_loads: 0,
      };
    }
  }

  async getRenderSnapshot(): Promise<RenderSceneSnapshot> {
    const bridge = getBridge();
    if (!bridge) {
      return {
        schema_version: "voxelforge.render_scene@1",
        revision: 0,
        model_id: "default",
        source: { host: "bridge", capabilities: [] },
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
    }

    try {
      // Try the new snapshot channel first
      const response = await bridge.request(
        "bridge:render-snapshot",
        {},
      ) as BridgeRequestResponse<RenderSceneSnapshot>;

      const data = (response as { data?: unknown }).data ?? response;

      // Check if this is a RenderSceneSnapshot
      if ((data as RenderSceneSnapshot).schema_version) {
        this._connected = true;
        return normalizeSnapshot(data as RenderSceneSnapshot);
      }
    } catch {
      // Fall through to transitional
    }

    // Fallback: bridge:mesh-snapshot + bridge:state-subscribe
    try {
      const [stateResponse, meshResponse] = await Promise.all([
        bridge.request("bridge:state-subscribe", {
          domains: ["document", "session"],
          delivery_mode: "snapshot",
          full_snapshot_on_subscribe: true,
        }),
        bridge.request("bridge:mesh-snapshot", {
          model_id: "",
          lod_level: 0,
          payload_format: "json",
          include_palette_mapping: true,
        }),
      ]);

      const stateData = (stateResponse as { snapshot?: TransitionalViewerState }).snapshot;
      const meshData = meshResponse as TransitionalMeshSnapshot;

      this._connected = true;
      return transitionalMeshToSnapshot(meshData, stateData ?? null);
    } catch {
      return {
        schema_version: "voxelforge.render_scene@1",
        revision: 0,
        model_id: "default",
        source: { host: "bridge", capabilities: [] },
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
    }
  }

  subscribeEvents(
    onEvent: (event: ViewerSseEvent) => void,
    onError?: (error: unknown) => void,
  ): () => void {
    const bridge = getBridge();
    if (!bridge) return () => {};

    // Subscribe to state-delta events which carry revision info
    const unsubState = bridge.onEvent(
      "voxelforge:state-delta",
      (payload: unknown) => {
        try {
          const delta = payload as {
            domain?: string;
            sequence?: number;
            full?: boolean;
            snapshot?: { revision?: number };
          };
          const revision = delta.snapshot?.revision ?? delta.sequence ?? 0;
          onEvent({
            type: "revision",
            revision,
          });
        } catch (e) {
          onError?.(e);
        }
      },
    );

    // Subscribe to mesh-update events which also indicate revision changes
    const unsubMesh = bridge.onEvent(
      "voxelforge:mesh-update",
      () => {
        onEvent({
          type: "revision",
          revision: Date.now(),
        });
      },
    );

    this.unsubscribeFns.push(unsubState, unsubMesh);

    return () => {
      unsubState();
      unsubMesh();
    };
  }

  async sendCommand(command: RenderCommand): Promise<void> {
    const bridge = getBridge();
    if (!bridge) return;

    switch (command.command) {
      case "frame_camera":
        // No bridge command needed — handled locally by the scene
        break;
      case "set_grid_visible":
        await bridge.request("bridge:set-grid-visible", {
          visible: command.params.visible,
        }).catch(() => {});
        break;
      case "set_wireframe":
        await bridge.request("bridge:set-wireframe", {
          visible: command.params.visible,
        }).catch(() => {});
        break;
      case "set_background_color":
        await bridge.request("bridge:set-background-color", {
          r: command.params.r,
          g: command.params.g,
          b: command.params.b,
        }).catch(() => {});
        break;
      case "capture_screenshot":
        await bridge.request("bridge:capture-screenshot", {
          width: command.params.width,
          height: command.params.height,
          format: command.params.format,
        }).catch(() => {});
        break;
    }
  }

  isConnected(): boolean {
    return this._connected;
  }

  /** Clean up all subscriptions. */
  dispose(): void {
    for (const unsub of this.unsubscribeFns) {
      unsub();
    }
    this.unsubscribeFns = [];
  }
}
