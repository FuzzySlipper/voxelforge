import WebSocket from "ws";
import {
  parseResponseFrame,
  parseEventFrame,
  type BridgeResponse,
  type BridgeError,
} from "../shared/frame-parser";

/**
 * Minimal raw WebSocket client for the den-bridge protocol.
 *
 * This is a temporary stand-in until upstream den-bridge TypeScript packages
 * are available. It speaks the JSON frame protocol directly.
 */
export interface BridgeClientOptions {
  endpoint: string;
  authToken: string;
}

export interface BridgeRequest {
  requestId: string;
  command: string;
  payload?: unknown;
}

// Re-export for consumer convenience.
export type { BridgeResponse, BridgeError };

export class BridgeClient {
  private socket: WebSocket | null = null;
  private pending = new Map<string, (response: BridgeResponse) => void>();
  private pendingReject = new Map<string, (reason: Error) => void>();
  private nextRequestId = 0;
  private connected = false;
  private connectError: Error | null = null;

  constructor(private readonly options: BridgeClientOptions) {}

  async connect(): Promise<void> {
    return new Promise((resolve, reject) => {
      const socket = new WebSocket(this.options.endpoint, {
        headers: {
          Authorization: `Bearer ${this.options.authToken}`,
        },
      });

      socket.on("open", () => {
        this.connected = true;
        this.connectError = null;
        resolve();
      });

      socket.on("error", (err) => {
        this.connectError = err;
        if (!this.connected) {
          reject(err);
        }
      });

      socket.on("message", (data) => {
        this.handleMessage(data.toString());
      });

      socket.on("close", () => {
        this.connected = false;
        this.rejectAllPending(new Error("Bridge WebSocket connection closed."));
      });

      this.socket = socket;
    });
  }

  async send(request: BridgeRequest, timeoutMs = 5000): Promise<BridgeResponse> {
    if (!this.connected || !this.socket) {
      throw new Error("Bridge client is not connected.");
    }

    const requestId = request.requestId || this.makeRequestId();
    const frame = {
      protocol_version: "1.0",
      schema_version: "den-bridge@1",
      frame_type: "request",
      request_id: requestId,
      command: request.command,
      payload: request.payload ?? {},
      correlation: {},
      sent_at: new Date().toISOString(),
    };

    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(requestId);
        this.pendingReject.delete(requestId);
        reject(new Error(`Bridge request '${requestId}' timed out after ${timeoutMs}ms.`));
      }, timeoutMs);

      this.pending.set(requestId, (response) => {
        clearTimeout(timer);
        this.pending.delete(requestId);
        this.pendingReject.delete(requestId);
        resolve(response);
      });

      this.pendingReject.set(requestId, (err) => {
        clearTimeout(timer);
        this.pending.delete(requestId);
        this.pendingReject.delete(requestId);
        reject(err);
      });

      this.socket!.send(JSON.stringify(frame), (err: Error | undefined) => {
        if (err) {
          clearTimeout(timer);
          this.pending.delete(requestId);
          this.pendingReject.delete(requestId);
          reject(err);
        }
      });
    });
  }

  disconnect(): void {
    if (this.socket) {
      this.socket.close();
      this.socket = null;
    }
    this.connected = false;
    this.rejectAllPending(new Error("Bridge client was disconnected by caller."));
  }

  isConnected(): boolean {
    return this.connected;
  }

  private eventHandlers = new Map<string, (payload: unknown) => void>();

  /**
   * Register a handler for a specific event type.
   * The handler receives the event payload.
   */
  onEvent(eventType: string, handler: (payload: unknown) => void): void {
    this.eventHandlers.set(eventType, handler);
  }

  /**
   * Remove a previously registered event handler.
   */
  offEvent(eventType: string): void {
    this.eventHandlers.delete(eventType);
  }

  private handleMessage(raw: string): void {
    // Use parseFrame to check valid JSON; fall through to shared frame parsers.
    let frame: unknown;
    try {
      frame = JSON.parse(raw);
    } catch {
      console.error("[bridge-client] Received invalid JSON:", raw.slice(0, 200));
      return;
    }

    // Try response frame first
    const response = parseResponseFrame(frame);
    if (response) {
      const resolve = this.pending.get(response.requestId);
      if (resolve) {
        resolve(response);
      }
      return;
    }

    // Try event frame
    const event = parseEventFrame(frame);
    if (event) {
      const handler = this.eventHandlers.get(event.eventType);
      if (handler) {
        handler(event.payload);
      } else {
        console.log(`[bridge-client] Unhandled event: ${event.eventType}`);
      }
      return;
    }
  }

  private makeRequestId(): string {
    return `req-${++this.nextRequestId}-${Date.now()}`;
  }

  private rejectAllPending(reason: Error): void {
    for (const reject of this.pendingReject.values()) {
      reject(reason);
    }
    this.pending.clear();
    this.pendingReject.clear();
  }
}
