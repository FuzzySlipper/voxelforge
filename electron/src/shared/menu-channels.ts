/**
 * IPC channel constants for native Electron menu events sent from main to renderer.
 * Each channel corresponds to a menu item click in the native menu bar.
 */
export const MenuChannels = {
  FILE_NEW: "menu:file-new",
  FILE_OPEN: "menu:file-open",
  FILE_SAVE: "menu:file-save",
  FILE_SAVE_AS: "menu:file-save-as",
  FILE_EXIT: "menu:file-exit",
  EDIT_UNDO: "menu:edit-undo",
  EDIT_REDO: "menu:edit-redo",
  EDIT_FILL_REGION: "menu:edit-fill-region",
  EDIT_PALETTE_LIST: "menu:edit-palette-list",
  EDIT_PALETTE_ADD: "menu:edit-palette-add",
  EDIT_REGIONS_LIST: "menu:edit-regions-list",
  EDIT_REGIONS_LABEL: "menu:edit-regions-label",
  EDIT_CLEAR_ALL: "menu:edit-clear-all",
  VIEW_FRONT: "menu:view-front",
  VIEW_SIDE: "menu:view-side",
  VIEW_TOP: "menu:view-top",
  VIEW_WIREFRAME: "menu:view-wireframe",
  VIEW_GRID_SIZE: "menu:view-grid-size",
  VIEW_MEASURE_GRID: "menu:view-measure-grid",
  VIEW_MEASURE_SCALE: "menu:view-measure-scale",
  VIEW_BG_COLOR: "menu:view-bg-color",

  // Reference model menu
  REFERENCE_MODEL_LOAD: "menu:reference-model-load",
  REFERENCE_MODEL_LIST: "menu:reference-model-list",
  REFERENCE_MODEL_REMOVE: "menu:reference-model-remove",
  REFERENCE_CLEAR: "menu:reference-clear",
  REFERENCE_TRANSFORM: "menu:reference-transform",
  REFERENCE_MODE: "menu:reference-mode",
  REFERENCE_VISIBILITY: "menu:reference-visibility",
  REFERENCE_SCALE: "menu:reference-scale",
  REFERENCE_ROTATE: "menu:reference-rotate",
  REFERENCE_ORIENT: "menu:reference-orient",
  REFERENCE_INFO: "menu:reference-info",
  REFERENCE_ANIMATION: "menu:reference-animation",
  REFERENCE_TEXTURE_ASSIGN: "menu:reference-texture-assign",
  REFERENCE_EMISSIVE_ASSIGN: "menu:reference-emissive-assign",
  REFERENCE_META_SAVE: "menu:reference-meta-save",
  REFERENCE_META_LOAD: "menu:reference-meta-load",

  // Image reference menu
  IMAGE_REF_LOAD: "menu:image-ref-load",
  IMAGE_REF_LIST: "menu:image-ref-list",
  IMAGE_REF_REMOVE: "menu:image-ref-remove",

  // Voxelize menu
  VOXELIZE_EXECUTE: "menu:voxelize-execute",
  VOXELIZE_COMPARE: "menu:voxelize-compare",

  HELP_ABOUT: "menu:help-about",
} as const;

export type MenuChannel = (typeof MenuChannels)[keyof typeof MenuChannels];
