import { contextBridge, ipcRenderer } from "electron";

/**
 * Preload script exposing safe IPC methods to the renderer process.
 * Only exposes explicitly-allowed channels — no full ipcRenderer bridge.
 */

const allowedChannels = [
  "bridge:handshake",
  "bridge:mesh-snapshot",
  "bridge:mesh-subscribe",
  "bridge:mesh-unsubscribe",
  "bridge:palette-get",
  "bridge:state-subscribe",
  "bridge:state-request-full",
  "bridge:command-execute",
  "bridge:history-undo",
  "bridge:history-redo",
  "bridge:project-save",
  "bridge:project-load",
  "bridge:ping",
  "bridge:version-handshake",
  "renderer:ready",
  "renderer:metrics",
] as const;

const allowedEventChannels = [
  "voxelforge:mesh-update",
  "voxelforge:palette-update",
  "voxelforge:state-delta",
] as const;

function validateChannel(channel: string): void {
  if (!(allowedChannels as readonly string[]).includes(channel)) {
    throw new Error(`IPC channel not allowed in preload: ${channel}`);
  }
}

function validateEventChannel(channel: string): void {
  if (!(allowedEventChannels as readonly string[]).includes(channel)) {
    throw new Error(`IPC event channel not allowed in preload: ${channel}`);
  }
}

contextBridge.exposeInMainWorld("voxelforgeBridge", {
  /**
   * Send a request to the C# sidecar via the main process bridge client.
   * Returns the response payload.
   */
  request(channel: string, payload: unknown): Promise<unknown> {
    validateChannel(channel);
    return ipcRenderer.invoke(channel, payload);
  },

  /**
   * Subscribe to a bridge event forwarded from the main process.
   * Returns a cleanup function that removes the listener.
   */
  onEvent(channel: string, callback: (payload: unknown) => void): () => void {
    validateEventChannel(channel);
    const handler = (_event: Electron.IpcRendererEvent, payload: unknown) => callback(payload);
    ipcRenderer.on(channel, handler);
    return () => ipcRenderer.removeListener(channel, handler);
  },

  /**
   * Notify the main process that the renderer is ready.
   */
  notifyReady(): void {
    ipcRenderer.send("renderer:ready");
  },

  /**
   * Send performance metrics from the renderer to the main process.
   */
  sendMetrics(metrics: Record<string, number>): void {
    ipcRenderer.send("renderer:metrics", metrics);
  },
});