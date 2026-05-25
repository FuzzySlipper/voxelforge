/**
 * Render protocol client interface.
 * Defines a transport-agnostic contract for fetching render state,
 * snapshots, and subscribing to render events.
 *
 * Implementations:
 * - HttpSseRenderClient (browser HTTP + EventSource)
 * - DenBridgeRenderClient (Electron preload bridge IPC)
 */

import type {
  RenderSceneSnapshot,
  RenderStateSummary,
  ViewerSseEvent,
  TransitionalViewerState,
  TransitionalMeshSnapshot,
  RenderCommand,
} from "../protocol/types";

export interface RenderProtocolClient {
  /**
   * Get a lightweight render state summary.
   * Prefers /api/render/state fallback to transitional /api/viewer-state.
   */
  getRenderState(): Promise<RenderStateSummary>;

  /**
   * Fetch the full versioned render-scene snapshot.
   * Uses /api/render/snapshot when available, fallback to transitional.
   */
  getRenderSnapshot(): Promise<RenderSceneSnapshot>;

  /**
   * Subscribe to render events (revision changes, etc.).
   * Returns an unsubscribe function.
   */
  subscribeEvents(
    onEvent: (event: ViewerSseEvent) => void,
    onError?: (error: unknown) => void,
  ): () => void;

  /**
   * Send a render command (frame camera, toggle grid, etc.).
   * May be a no-op if the transport doesn't support commands.
   */
  sendCommand?(command: RenderCommand): Promise<void>;

  /**
   * Check if the transport is connected/available.
   */
  isConnected(): boolean;
}

/**
 * Snapshot of connection state for diagnostics.
 */
export interface ConnectionState {
  connected: boolean;
  transport: "http" | "bridge" | "none";
  last_error: string | null;
  revision: number;
  last_poll_ms: number;
}

/**
 * Response type from bridge request endpoints.
 */
export interface BridgeRequestResponse<T = unknown> {
  success: boolean;
  data: T;
  error?: string;
}
