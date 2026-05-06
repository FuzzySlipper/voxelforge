import { describe, it, expect } from "vitest";
import {
  isObject,
  parseFrame,
  getFrameType,
  parseResponseFrame,
  parseEventFrame,
} from "../src/shared/frame-parser";

describe("isObject", () => {
  it("returns true for a plain object", () => {
    expect(isObject({})).toBe(true);
    expect(isObject({ key: "value" })).toBe(true);
  });

  it("returns false for null", () => {
    expect(isObject(null)).toBe(false);
  });

  it("returns false for arrays", () => {
    expect(isObject([])).toBe(false);
    expect(isObject([1, 2, 3])).toBe(false);
  });

  it("returns false for primitives", () => {
    expect(isObject("string")).toBe(false);
    expect(isObject(42)).toBe(false);
    expect(isObject(true)).toBe(false);
    expect(isObject(undefined)).toBe(false);
  });
});

describe("parseFrame", () => {
  it("parses valid JSON", () => {
    const result = parseFrame('{"frame_type":"response","request_id":"req-1"}');
    expect(result).toEqual({ frame_type: "response", request_id: "req-1" });
  });

  it("returns null for invalid JSON", () => {
    expect(parseFrame("not json")).toBeNull();
    expect(parseFrame("{broken")).toBeNull();
  });

  it("returns null for empty string", () => {
    expect(parseFrame("")).toBeNull();
  });
});

describe("getFrameType", () => {
  it("extracts frame_type from a valid frame", () => {
    expect(getFrameType({ frame_type: "response" })).toBe("response");
    expect(getFrameType({ frame_type: "event" })).toBe("event");
  });

  it("returns null for non-object input", () => {
    expect(getFrameType(null)).toBeNull();
    expect(getFrameType("string")).toBeNull();
  });

  it("returns null when frame_type is not a string", () => {
    expect(getFrameType({ frame_type: 42 })).toBeNull();
  });

  it("returns null when frame_type is missing", () => {
    expect(getFrameType({})).toBeNull();
  });
});

describe("parseResponseFrame", () => {
  it("parses a minimal response frame", () => {
    const frame = {
      frame_type: "response",
      request_id: "req-1",
      result: { data: "ok" },
    };
    const result = parseResponseFrame(frame);
    expect(result).not.toBeNull();
    expect(result!.requestId).toBe("req-1");
    expect(result!.result).toEqual({ data: "ok" });
    expect(result!.error).toBeUndefined();
  });

  it("parses a response with error", () => {
    const frame = {
      frame_type: "response",
      request_id: "req-2",
      result: null,
      error: {
        code: "voxelforge.mesh.snapshot_failed",
        message: "Mesh generation failed",
        category: "internal",
        retryable: false,
      },
    };
    const result = parseResponseFrame(frame);
    expect(result).not.toBeNull();
    expect(result!.requestId).toBe("req-2");
    expect(result!.result).toBeNull();
    expect(result!.error).toEqual({
      code: "voxelforge.mesh.snapshot_failed",
      message: "Mesh generation failed",
      category: "internal",
      retryable: false,
    });
  });

  it("returns null for event frames", () => {
    const frame = { frame_type: "event", event: "voxelforge:state-delta" };
    expect(parseResponseFrame(frame)).toBeNull();
  });

  it("returns null for non-object input", () => {
    expect(parseResponseFrame("string")).toBeNull();
  });

  it("handles missing request_id gracefully", () => {
    const frame = { frame_type: "response" };
    const result = parseResponseFrame(frame);
    expect(result).not.toBeNull();
    expect(result!.requestId).toBe("");
  });
});

describe("parseEventFrame", () => {
  it("parses a minimal event frame", () => {
    const frame = {
      frame_type: "event",
      event: "voxelforge:state-delta",
      payload: { domain: "document" },
    };
    const result = parseEventFrame(frame);
    expect(result).not.toBeNull();
    expect(result!.eventType).toBe("voxelforge:state-delta");
    expect(result!.payload).toEqual({ domain: "document" });
  });

  it("parses an event with null payload", () => {
    const frame = {
      frame_type: "event",
      event: "voxelforge:health",
      payload: null,
    };
    const result = parseEventFrame(frame);
    expect(result).not.toBeNull();
    expect(result!.eventType).toBe("voxelforge:health");
    expect(result!.payload).toBeNull();
  });

  it("parses an event with missing payload", () => {
    const frame = {
      frame_type: "event",
      event: "voxelforge:simple",
    };
    const result = parseEventFrame(frame);
    expect(result).not.toBeNull();
    expect(result!.eventType).toBe("voxelforge:simple");
    expect(result!.payload).toBeUndefined();
  });

  it("returns null for response frames", () => {
    const frame = { frame_type: "response", request_id: "req-1" };
    expect(parseEventFrame(frame)).toBeNull();
  });

  it("returns null for non-object input", () => {
    expect(parseEventFrame(42)).toBeNull();
  });

  it("returns null when event field is missing", () => {
    const frame = { frame_type: "event" };
    expect(parseEventFrame(frame)).toBeNull();
  });

  it("returns null when event field is empty string", () => {
    const frame = { frame_type: "event", event: "" };
    expect(parseEventFrame(frame)).toBeNull();
  });
});

describe("end-to-end: frame parsing from raw strings", () => {
  it("parses a response frame from raw JSON string", () => {
    const raw = JSON.stringify({
      protocol_version: "1.0",
      frame_type: "response",
      request_id: "req-42",
      result: { hello: "world" },
      sent_at: "2025-01-01T00:00:00Z",
    });
    const frame = parseFrame(raw);
    expect(frame).not.toBeNull();
    const response = parseResponseFrame(frame!);
    expect(response).not.toBeNull();
    expect(response!.requestId).toBe("req-42");
    expect(response!.result).toEqual({ hello: "world" });
  });

  it("parses an event frame from raw JSON string", () => {
    const raw = JSON.stringify({
      protocol_version: "1.0",
      frame_type: "event",
      event: "voxelforge:mesh-update",
      payload: {
        model_id: "test",
        base_mesh_id: "mesh-1",
        sequence: 5,
        update_type: "incremental",
        changed_regions: [],
        payload_format: "json",
        full_vertex_count: 100,
        full_index_count: 300,
      },
    });
    const frame = parseFrame(raw);
    expect(frame).not.toBeNull();
    const event = parseEventFrame(frame!);
    expect(event).not.toBeNull();
    expect(event!.eventType).toBe("voxelforge:mesh-update");
    expect(event!.payload).toBeTypeOf("object");
    expect((event!.payload as Record<string, unknown>).model_id).toBe("test");
    expect((event!.payload as Record<string, unknown>).sequence).toBe(5);
  });

  it("returns null for gibberish input", () => {
    expect(parseFrame("not json at all {{{")).toBeNull();
  });
});
