/**
 * menu-command-model.ts — Shared menu/command model
 *
 * Single source of truth for menu structure consumed by:
 *   1) Renderer-owned accessible menu surface (accessible-menu-surface.ts)
 *   2) Native Electron menu fallback (main/menu.ts)
 *   3) Tests / contract assertions
 *
 * Prevents divergent menu definitions. Every menu item that routes a workflow
 * command has a `channel` matching a menu:* IPC channel from menu-channels.ts.
 */
import { MenuChannels } from "./menu-channels";

// ── Types ──

export interface MenuCommandItem {
  /** Unique id matching the menu:* IPC channel constant key. */
  id: string;
  /** Human-readable label (accessibility name). */
  label: string;
  /** Type of menu item. */
  type: "item" | "separator";
  /** IPC channel sent when this item is activated; omitted for separators. */
  channel?: string;
  /** Keyboard accelerator hint (display only; actual binding is in main process). */
  accelerator?: string;
  /** Whether the item starts enabled/disabled. */
  enabled?: boolean;
}

export interface MenuCommandGroup {
  /** Unique id for this group/top-level menu. */
  id: string;
  /** Human-readable label (role="menuitem" / aria-label). */
  label: string;
  /** Items in this menu. */
  items: MenuCommandItem[];
}

export type MenuCommandModel = MenuCommandGroup[];

// ── The canonical menu model ──

/**
 * Canonical application menu model.
 *
 * This is the single authoritative definition. Both the renderer accessible
 * menu surface and the native menu (when retained) consume this array.
 *
 * Channel values map to MenuChannels constants. Activation of any item
 * triggers the menu:* IPC event flow → preload → renderer handler.
 */
export const APP_MENU_MODEL: MenuCommandModel = [
  {
    id: "file",
    label: "File",
    items: [
      { id: "file-new", label: "New", type: "item", channel: MenuChannels.FILE_NEW, accelerator: "Ctrl+N", enabled: true },
      { id: "file-open", label: "Open…", type: "item", channel: MenuChannels.FILE_OPEN, accelerator: "Ctrl+O", enabled: true },
      { id: "file-sep-1", label: "", type: "separator" },
      { id: "file-save", label: "Save", type: "item", channel: MenuChannels.FILE_SAVE, accelerator: "Ctrl+S", enabled: true },
      { id: "file-save-as", label: "Save As…", type: "item", channel: MenuChannels.FILE_SAVE_AS, accelerator: "Ctrl+Shift+S", enabled: true },
      { id: "file-sep-2", label: "", type: "separator" },
      { id: "file-exit", label: "Exit", type: "item", channel: MenuChannels.FILE_EXIT, accelerator: "Alt+F4", enabled: true },
    ],
  },
  {
    id: "edit",
    label: "Edit",
    items: [
      { id: "edit-undo", label: "Undo", type: "item", channel: MenuChannels.EDIT_UNDO, accelerator: "Ctrl+Z", enabled: true },
      { id: "edit-redo", label: "Redo", type: "item", channel: MenuChannels.EDIT_REDO, accelerator: "Ctrl+Shift+Z", enabled: true },
      { id: "edit-sep-1", label: "", type: "separator" },
      { id: "edit-fill-region", label: "Fill Region…", type: "item", channel: MenuChannels.EDIT_FILL_REGION, enabled: true },
      { id: "edit-sep-2", label: "", type: "separator" },
      {
        id: "edit-palette-group", label: "Palette", type: "item",
        channel: MenuChannels.EDIT_PALETTE_LIST, enabled: true,
      },
      { id: "edit-palette-add", label: "Add Material…", type: "item", channel: MenuChannels.EDIT_PALETTE_ADD, enabled: true },
      { id: "edit-sep-3", label: "", type: "separator" },
      { id: "edit-regions-list", label: "List Regions", type: "item", channel: MenuChannels.EDIT_REGIONS_LIST, enabled: true },
      { id: "edit-regions-label", label: "Label Voxel…", type: "item", channel: MenuChannels.EDIT_REGIONS_LABEL, enabled: true },
      { id: "edit-sep-4", label: "", type: "separator" },
      { id: "edit-clear-all", label: "Clear All", type: "item", channel: MenuChannels.EDIT_CLEAR_ALL, enabled: true },
    ],
  },
  {
    id: "reference",
    label: "Reference",
    items: [
      { id: "ref-model-load", label: "Load Reference Model…", type: "item", channel: MenuChannels.REFERENCE_MODEL_LOAD, enabled: true },
      { id: "ref-model-list", label: "List Reference Models", type: "item", channel: MenuChannels.REFERENCE_MODEL_LIST, enabled: true },
      { id: "ref-model-remove", label: "Remove Reference Model…", type: "item", channel: MenuChannels.REFERENCE_MODEL_REMOVE, enabled: true },
      { id: "ref-clear", label: "Clear All References", type: "item", channel: MenuChannels.REFERENCE_CLEAR, enabled: true },
      { id: "ref-sep-1", label: "", type: "separator" },
      { id: "ref-transform", label: "Transform (Position)…", type: "item", channel: MenuChannels.REFERENCE_TRANSFORM, enabled: true },
      { id: "ref-mode", label: "Render Mode…", type: "item", channel: MenuChannels.REFERENCE_MODE, enabled: true },
      { id: "ref-visibility", label: "Toggle Visibility…", type: "item", channel: MenuChannels.REFERENCE_VISIBILITY, enabled: true },
      { id: "ref-scale", label: "Scale…", type: "item", channel: MenuChannels.REFERENCE_SCALE, enabled: true },
      { id: "ref-rotate", label: "Rotate…", type: "item", channel: MenuChannels.REFERENCE_ROTATE, enabled: true },
      { id: "ref-orient", label: "Auto-Orient", type: "item", channel: MenuChannels.REFERENCE_ORIENT, enabled: true },
      { id: "ref-info", label: "Info / Inspect…", type: "item", channel: MenuChannels.REFERENCE_INFO, enabled: true },
      { id: "ref-sep-2", label: "", type: "separator" },
      { id: "ref-texture-assign", label: "Texture Assignment…", type: "item", channel: MenuChannels.REFERENCE_TEXTURE_ASSIGN, enabled: true },
      { id: "ref-emissive-assign", label: "Emissive Texture…", type: "item", channel: MenuChannels.REFERENCE_EMISSIVE_ASSIGN, enabled: true },
      { id: "ref-sep-3", label: "", type: "separator" },
      { id: "ref-anim-list", label: "List Animation Clips", type: "item", channel: MenuChannels.REFERENCE_ANIMATION, enabled: true },
      { id: "ref-sep-4", label: "", type: "separator" },
      { id: "ref-meta-save", label: "Save Meta…", type: "item", channel: MenuChannels.REFERENCE_META_SAVE, enabled: true },
      { id: "ref-meta-load", label: "Load Meta…", type: "item", channel: MenuChannels.REFERENCE_META_LOAD, enabled: true },
      { id: "ref-sep-5", label: "", type: "separator" },
      { id: "img-ref-load", label: "Image Ref Load…", type: "item", channel: MenuChannels.IMAGE_REF_LOAD, enabled: true },
      { id: "img-ref-list", label: "Image Ref List", type: "item", channel: MenuChannels.IMAGE_REF_LIST, enabled: true },
      { id: "img-ref-remove", label: "Image Ref Remove…", type: "item", channel: MenuChannels.IMAGE_REF_REMOVE, enabled: true },
    ],
  },
  {
    id: "view",
    label: "View",
    items: [
      { id: "view-front", label: "Front", type: "item", channel: MenuChannels.VIEW_FRONT, accelerator: "F1", enabled: true },
      { id: "view-side", label: "Side", type: "item", channel: MenuChannels.VIEW_SIDE, accelerator: "F2", enabled: true },
      { id: "view-top", label: "Top", type: "item", channel: MenuChannels.VIEW_TOP, accelerator: "F3", enabled: true },
      { id: "view-sep-1", label: "", type: "separator" },
      { id: "view-wireframe", label: "Wireframe Toggle", type: "item", channel: MenuChannels.VIEW_WIREFRAME, accelerator: "F4", enabled: true },
      { id: "view-sep-2", label: "", type: "separator" },
      { id: "view-grid-size", label: "Grid Size…", type: "item", channel: MenuChannels.VIEW_GRID_SIZE, enabled: true },
      { id: "view-measure-grid", label: "Measure Grid Toggle", type: "item", channel: MenuChannels.VIEW_MEASURE_GRID, accelerator: "F5", enabled: true },
      { id: "view-measure-scale", label: "Measure Scale…", type: "item", channel: MenuChannels.VIEW_MEASURE_SCALE, enabled: true },
      { id: "view-sep-3", label: "", type: "separator" },
      { id: "view-bg-color", label: "Background Color…", type: "item", channel: MenuChannels.VIEW_BG_COLOR, enabled: true },
    ],
  },
  {
    id: "tools",
    label: "Tools",
    items: [
      { id: "voxelize", label: "Voxelize…", type: "item", channel: MenuChannels.VOXELIZE_EXECUTE, enabled: true },
      { id: "voxelize-compare", label: "Voxelize & Compare…", type: "item", channel: MenuChannels.VOXELIZE_COMPARE, enabled: true },
      { id: "tools-sep-1", label: "", type: "separator" },
      { id: "command-palette", label: "Command Palette…", type: "item", channel: MenuChannels.COMMAND_PALETTE, accelerator: "Ctrl+Shift+P", enabled: true },
      { id: "tools-sep-2", label: "", type: "separator" },
      { id: "cmd-ao-bake", label: "Bake AO", type: "item", channel: MenuChannels.COMMAND_PALETTE_AO_BAKE, enabled: true },
      { id: "cmd-edge-darken", label: "Edge Darken", type: "item", channel: MenuChannels.COMMAND_PALETTE_EDGE_DARKEN, enabled: true },
      { id: "cmd-light-bake", label: "Light Bake", type: "item", channel: MenuChannels.COMMAND_PALETTE_LIGHT_BAKE, enabled: true },
      { id: "cmd-palette-map", label: "Palette Map…", type: "item", channel: MenuChannels.COMMAND_PALETTE_PALETTE_MAP, enabled: true },
      { id: "cmd-palette-reduce", label: "Palette Reduce…", type: "item", channel: MenuChannels.COMMAND_PALETTE_PALETTE_REDUCE, enabled: true },
      { id: "tools-sep-3", label: "", type: "separator" },
      { id: "cmd-screenshot", label: "Screenshot…", type: "item", channel: MenuChannels.COMMAND_PALETTE_SCREENSHOT, enabled: true },
    ],
  },
  {
    id: "help",
    label: "Help",
    items: [
      { id: "help-about", label: "About VoxelForge", type: "item", channel: MenuChannels.HELP_ABOUT, enabled: true },
    ],
  },
];

// ── Lookup helpers ──

/** Find a menu item by its id (e.g. "ref-model-load"). */
export function findMenuItemById(
  model: MenuCommandModel,
  id: string,
): { group: MenuCommandGroup; item: MenuCommandItem } | undefined {
  for (const group of model) {
    const item = group.items.find((i) => i.id === id);
    if (item) return { group, item };
  }
  return undefined;
}

/** Find a menu group by its id (e.g. "reference"). */
export function findMenuGroupById(
  model: MenuCommandModel,
  id: string,
): MenuCommandGroup | undefined {
  return model.find((g) => g.id === id);
}

/** Get all enabled (non-separator) items across all groups. */
export function getAllEnabledItems(model: MenuCommandModel): MenuCommandItem[] {
  const result: MenuCommandItem[] = [];
  for (const group of model) {
    for (const item of group.items) {
      if (item.type === "item" && item.enabled !== false) {
        result.push(item);
      }
    }
  }
  return result;
}
