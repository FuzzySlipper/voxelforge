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
  HELP_ABOUT: "menu:help-about",
} as const;

export type MenuChannel = (typeof MenuChannels)[keyof typeof MenuChannels];
