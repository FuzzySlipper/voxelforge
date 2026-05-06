/**
 * Title-case a snake_case or lower-case string.
 * e.g. "place_voxel" → "Place_voxel", "place" → "Place"
 */
export function titleCase(value: string): string {
  return value.length === 0 ? value : value[0].toUpperCase() + value.slice(1);
}

/**
 * Format an unknown error value to a readable string.
 */
export function formatError(err: unknown): string {
  return err instanceof Error ? err.message : String(err);
}

/**
 * Escape HTML special characters for safe innerHTML assignment.
 */
export function escapeHtml(value: string): string {
  return value
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;");
}
