import WebSocket from "ws";

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

export interface BridgeResponse {
  requestId: string;
  result?: unknown;
  error?: BridgeError;
}

export interface BridgeError {
  code: string;
  message: string;
  category: string;
  retryable?: boolean;
}

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

  private handleMessage(raw: string): void {
    let frame: unknown;
    try {
      frame = JSON.parse(raw);
    } catch {
      console.error("[bridge-client] Received invalid JSON:", raw.slice(0, 200));
      return;
    }

    if (!isObject(frame) || frame.frame_type !== "response") {
      // Ignore non-response frames in the smoke test.
      return;
    }

    const response: BridgeResponse = {
      requestId: String((frame as Record<string, unknown>).request_id ?? ""),
      result: (frame as Record<string, unknown>).result as unknown,
      error: (frame as Record<string, unknown>).error as BridgeError | undefined,
    };

    const resolve = this.pending.get(response.requestId);
    if (resolve) {
      resolve(response);
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

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}
