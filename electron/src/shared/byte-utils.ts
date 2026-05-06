/**
 * Decode a byte array payload from the C# bridge.
 * C# `byte[]` serializes as a base64 string over JSON (System.Text.Json default).
 * This handles both formats: base64 string or number[]/number-like arrays.
 * Returns a plain number[] for backward compatibility.
 */
export function decodeByteArray(
  value: string | number[] | Uint8Array | undefined | null,
  expectedLength?: number,
): number[] {
  if (value === null || value === undefined) {
    return [];
  }
  if (typeof value === "string") {
    // Base64-encoded byte array from C# System.Text.Json
    const binaryStr = atob(value);
    const bytes = new Uint8Array(binaryStr.length);
    for (let i = 0; i < binaryStr.length; i++) {
      bytes[i] = binaryStr.charCodeAt(i);
    }
    if (expectedLength !== undefined && bytes.length !== expectedLength) {
      console.warn(
        `[byte-utils] Byte array length mismatch: expected ${expectedLength}, got ${bytes.length}`,
      );
    }
    return Array.from(bytes);
  }
  if (Array.isArray(value)) {
    return value;
  }
  if (value instanceof Uint8Array) {
    return Array.from(value);
  }
  console.warn("[byte-utils] Unknown byte array format:", typeof value);
  return [];
}
