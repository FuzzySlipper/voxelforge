import { contextBridge, ipcRenderer } from "electron";

/**
 * Preload script exposing safe IPC methods to the renderer process.
 * Only exposes explicitly-allowed channels — no full ipcRenderer bridge.
 */

const allowedChannels = [
  "bridge:handshake",
  "bridge:mesh-snapshot",
  "bridge:palette-get",
  "bridge:ping",
  "bridge:version-handshake",
  "renderer:ready",
  "renderer:metrics",
] as const;

function validateChannel(channel: string): void {
  if (!(allowedChannels as readonly string[]).includes(channel)) {
    throw new Error(`IPC channel not allowed in preload: ${channel}`);
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