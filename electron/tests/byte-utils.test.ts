import { describe, it, expect, vi } from "vitest";
import { decodeByteArray } from "../src/shared/byte-utils";

describe("decodeByteArray", () => {
  it("returns empty array for null input", () => {
    expect(decodeByteArray(null)).toEqual([]);
  });

  it("returns empty array for undefined input", () => {
    expect(decodeByteArray(undefined)).toEqual([]);
  });

  it("passes through a plain number[] unchanged", () => {
    const input = [1, 2, 3, 255, 0, 128];
    expect(decodeByteArray(input)).toEqual(input);
  });

  it("passes through an empty number[]", () => {
    expect(decodeByteArray([])).toEqual([]);
  });

  it("converts a Uint8Array to number[]", () => {
    const input = new Uint8Array([10, 20, 30, 255]);
    expect(decodeByteArray(input)).toEqual([10, 20, 30, 255]);
  });

  it("decodes a base64-encoded byte string", () => {
    // [128, 128, 128, 255] as base64
    const base64 = btoa(String.fromCharCode(128, 128, 128, 255));
    expect(decodeByteArray(base64)).toEqual([128, 128, 128, 255]);
  });

  it("decodes a longer base64 string correctly", () => {
    const bytes: number[] = [];
    for (let i = 0; i < 32; i++) bytes.push(i);
    const base64 = btoa(String.fromCharCode(...bytes));
    expect(decodeByteArray(base64)).toEqual(bytes);
  });

  it("returns empty array for non-array/non-string non-object input", () => {
    // The function type signature doesn't allow number, but handling it gracefully
    expect(decodeByteArray(42 as unknown as string)).toEqual([]);
  });

  it("logs a warning for base64 length mismatch", () => {
    const warnSpy = vi.spyOn(console, "warn").mockImplementation(() => {});
    const base64 = btoa(String.fromCharCode(1, 2, 3));
    const result = decodeByteArray(base64, 100);
    expect(result).toEqual([1, 2, 3]);
    expect(warnSpy).toHaveBeenCalledTimes(1);
    expect(warnSpy).toHaveBeenCalledWith(
      expect.stringContaining("expected 100, got 3"),
    );
    warnSpy.mockRestore();
  });

  it("handles empty base64 string", () => {
    expect(decodeByteArray("")).toEqual([]);
  });
});
