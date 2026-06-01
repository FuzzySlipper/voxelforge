/**
 * dialog-types.ts — Typed, allowlisted dialog request/response types for
 * Electron native file picker dialogs.
 *
 * The main process only accepts these specific dialog kinds. No arbitrary
 * filesystem, shell, or generic IPC is exposed to the renderer.
 */

/** Supported dialog kinds — each maps to a specific Electron dialog configuration. */
export type DialogKind =
  | "vforge-open"
  | "vforge-save"
  | "reference-model-open"
  | "refmeta-open"
  | "refmeta-save"
  | "image-open"
  | "texture-open";

/** Request payload sent from renderer to main for a file dialog. */
export interface DialogRequest {
  kind: DialogKind;
  /** Suggested default path (optional). */
  defaultPath?: string;
}

/** Response returned from main to renderer after a file dialog. */
export interface DialogResponse {
  /** Whether the user selected a file (true) or cancelled (false). */
  canceled: boolean;
  /** Absolute file path(s) selected. Empty array if canceled. */
  filePaths: string[];
}

/** IPC channel names for dialog requests. */
export const DialogChannels = {
  SELECT_FILE: "dialog:select-file",
  SAVE_FILE: "dialog:save-file",
} as const;

export type DialogChannel = (typeof DialogChannels)[keyof typeof DialogChannels];

/**
 * Get the Electron dialog configuration for a given dialog kind.
 * Returns the filters and properties for showOpenDialog / showSaveDialog.
 */
export function getOpenDialogConfig(kind: DialogKind): {
  filters: Electron.FileFilter[];
  properties: ("openFile" | "multiSelections")[];
} {
  switch (kind) {
    case "vforge-open":
      return {
        filters: [{ name: "VoxelForge Project", extensions: ["vforge"] }],
        properties: ["openFile"],
      };
    case "reference-model-open":
      return {
        filters: [
          {
            name: "3D Model",
            extensions: ["obj", "fbx", "gltf", "glb", "stl", "dae", "x", "3ds", "blend"],
          },
        ],
        properties: ["openFile"],
      };
    case "refmeta-open":
      return {
        filters: [{ name: "Reference Meta", extensions: ["refmeta"] }],
        properties: ["openFile"],
      };
    case "image-open":
      return {
        filters: [
          { name: "Image", extensions: ["png", "jpg", "jpeg", "bmp", "gif", "webp", "tga", "hdr"] },
        ],
        properties: ["openFile"],
      };
    case "texture-open":
      return {
        filters: [
          { name: "Texture Image", extensions: ["png", "jpg", "jpeg", "bmp", "gif", "webp", "tga", "hdr"] },
        ],
        properties: ["openFile"],
      };
    default:
      throw new Error(`Unknown open dialog kind: ${kind}`);
  }
}

/**
 * Get the Electron save dialog configuration for a given dialog kind.
 */
export function getSaveDialogConfig(kind: DialogKind): {
  filters: Electron.FileFilter[];
} {
  switch (kind) {
    case "vforge-save":
      return {
        filters: [{ name: "VoxelForge Project", extensions: ["vforge"] }],
      };
    case "refmeta-save":
      return {
        filters: [{ name: "Reference Meta", extensions: ["refmeta"] }],
      };
    default:
      throw new Error(`Unknown save dialog kind: ${kind}`);
  }
}

/** Set of all valid dialog kinds. */
export const VALID_DIALOG_KINDS = new Set<DialogKind>([
  "vforge-open",
  "vforge-save",
  "reference-model-open",
  "refmeta-open",
  "refmeta-save",
  "image-open",
  "texture-open",
]);

/** Dialog kinds that are valid for showOpenDialog. */
export const OPEN_DIALOG_KINDS = new Set<DialogKind>([
  "vforge-open",
  "reference-model-open",
  "refmeta-open",
  "image-open",
  "texture-open",
]);

/** Dialog kinds that are valid for showSaveDialog. */
export const SAVE_DIALOG_KINDS = new Set<DialogKind>([
  "vforge-save",
  "refmeta-save",
]);
