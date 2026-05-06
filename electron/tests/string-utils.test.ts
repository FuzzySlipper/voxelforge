import { describe, it, expect } from "vitest";
import { titleCase, formatError, escapeHtml } from "../src/shared/string-utils";

describe("titleCase", () => {
  it("capitalizes first letter of a regular word", () => {
    expect(titleCase("hello")).toBe("Hello");
  });

  it("capitalizes first letter of snake_case", () => {
    expect(titleCase("place_voxel")).toBe("Place_voxel");
  });

  it("returns empty string for empty input", () => {
    expect(titleCase("")).toBe("");
  });

  it("does not change already-capitalized first letter", () => {
    expect(titleCase("Hello")).toBe("Hello");
  });

  it("preserves single-character input", () => {
    expect(titleCase("a")).toBe("A");
    expect(titleCase("A")).toBe("A");
  });

  it("preserves the rest of the string unchanged", () => {
    expect(titleCase("set_active_tool")).toBe("Set_active_tool");
  });
});

describe("formatError", () => {
  it("returns Error.message for Error instances", () => {
    expect(formatError(new Error("something broke"))).toBe("something broke");
  });

  it("returns String() for non-Error values", () => {
    expect(formatError("just a string")).toBe("just a string");
    expect(formatError(42)).toBe("42");
    expect(formatError(null)).toBe("null");
    expect(formatError(undefined)).toBe("undefined");
    expect(formatError({})).toBe("[object Object]");
  });

  it("includes stack trace as .message (not the whole stack)", () => {
    const err = new TypeError("type mismatch");
    expect(formatError(err)).toBe("type mismatch");
  });
});

describe("escapeHtml", () => {
  it("replaces & with &amp;", () => {
    expect(escapeHtml("a & b")).toBe("a &amp; b");
  });

  it("replaces < with &lt;", () => {
    expect(escapeHtml("<tag>")).toBe("&lt;tag&gt;");
  });

  it("replaces > with &gt;", () => {
    expect(escapeHtml("a > b")).toBe("a &gt; b");
  });

  it("handles all three special characters together", () => {
    expect(escapeHtml("<div class=\"foo\">Tom & Jerry</div>")).toBe(
      "&lt;div class=\"foo\"&gt;Tom &amp; Jerry&lt;/div&gt;",
    );
  });

  it("leaves normal text unchanged", () => {
    expect(escapeHtml("hello world")).toBe("hello world");
  });

  it("handles empty string", () => {
    expect(escapeHtml("")).toBe("");
  });
});
