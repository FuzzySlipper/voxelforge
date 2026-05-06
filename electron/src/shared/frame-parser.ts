/**
 * Response type parsed from a den-bridge response frame.
 */
export interface BridgeResponse {
  requestId: string;
  result?: unknown;
  error?: BridgeError | undefined;
}

/**
 * Error type from a den-bridge error frame.
 */
export interface BridgeError {
  code: string;
  message: string;
  category: string;
  retryable?: boolean;
}

/**
 * Type guard: check if a value is a non-null, non-array object.
 */
export function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

/**
 * Parse a raw den-bridge frame string.
 * Returns the parsed frame if valid, or null if the JSON is invalid.
 * Does not validate frame structure beyond parseability.
 */
export function parseFrame(raw: string): unknown {
  try {
    return JSON.parse(raw);
  } catch {
    return null;
  }
}

/**
 * Extract the frame type from a parsed den-bridge frame object.
 * Returns null if the frame is not an object or has no frame_type field.
 */
export function getFrameType(frame: unknown): string | null {
  if (!isObject(frame)) return null;
  const ft = (frame as Record<string, unknown>).frame_type;
  return typeof ft === "string" ? ft : null;
}

/**
 * Parse a den-bridge response frame into a typed BridgeResponse.
 * Returns null if the frame is not a valid response frame.
 */
export function parseResponseFrame(frame: unknown): BridgeResponse | null {
  const ft = getFrameType(frame);
  if (ft !== "response") return null;
  if (!isObject(frame)) return null;

  const response: BridgeResponse = {
    requestId: String((frame as Record<string, unknown>).request_id ?? ""),
    result: (frame as Record<string, unknown>).result as unknown,
    error: (frame as Record<string, unknown>).error as BridgeError | undefined,
  };

  return response;
}

/**
 * Parse a den-bridge event frame into an event type and payload.
 * Returns null if the frame is not a valid event frame.
 */
export function parseEventFrame(
  frame: unknown,
): { eventType: string; payload: unknown } | null {
  const ft = getFrameType(frame);
  if (ft !== "event") return null;
  if (!isObject(frame)) return null;

  const eventType = String((frame as Record<string, unknown>).event ?? "");
  if (!eventType) return null;

  const payload = (frame as Record<string, unknown>).payload;
  return { eventType, payload };
}
