import { app, Menu, MenuItemConstructorOptions, BrowserWindow } from "electron";
import { MenuChannels } from "../shared/menu-channels";
import { APP_MENU_MODEL } from "../shared/menu-command-model";

/**
 * Build and set the native Electron application menu for the canonical JS renderer shell.
 *
 * NATIVE MENU POSTURE (per #1740/#1743):
 *   This native menu is REDUCED to essential OS-level items and keyboard shortcuts.
 *   Primary workflow commands (Reference, Tools, View, most Edit/File items) are now
 *   served by the renderer-owned accessible menu surface (AccessibleMenuSurface with
 *   role="menubar", role="menuitem", keyboard navigation).
 *
 *   Rationale for retention:
 *     1. OS keyboard accelerators (Cmd+Q, Cmd+W, Alt+F4) must work regardless of
 *        renderer state.
 *     2. macOS standard app menu (About, Hide, Quit) is required for platform conformity.
 *     3. Users on screen readers that natively interact with the OS menu bar still
 *        need the native menu as a fallback.
 *
 *   Shared model: APP_MENU_MODEL in src/shared/menu-command-model.ts is the canonical
 *   menu structure consumed by the renderer surface AND by any retained native menu items.
 *   Changes to menu structure should be made in the model only; both surfaces auto-sync.
 *
 *   Menu clicks send IPC events to the renderer, which wires them to existing bridge commands
 *   (runAction / executeCommand) and may show dialogs natively in the renderer.
 */
export function setupMenu(mainWindow: BrowserWindow): void {
  const template: MenuItemConstructorOptions[] = [
    buildFileMenu(mainWindow),
    buildEditMenu(mainWindow),
    buildReferenceMenu(mainWindow),
    buildViewMenu(mainWindow),
    buildToolsMenu(mainWindow),
    buildHelpMenu(mainWindow),
  ];

  // macOS: put standard app menu first
  if (process.platform === "darwin") {
    template.unshift({
      label: app.name,
      submenu: [
        { role: "about" },
        { type: "separator" },
        { role: "services" },
        { type: "separator" },
        { role: "hide" },
        { role: "hideOthers" },
        { role: "unhide" },
        { type: "separator" },
        { role: "quit" },
      ],
    });
  }

  const menu = Menu.buildFromTemplate(template);
  Menu.setApplicationMenu(menu);
}

function send(win: BrowserWindow, channel: string, payload?: unknown): void {
  if (win && !win.isDestroyed()) {
    win.webContents.send(channel, payload);
  }
}

function buildFileMenu(win: BrowserWindow): MenuItemConstructorOptions {
  return {
    label: "&File",
    submenu: [
      {
        label: "&New",
        accelerator: "CmdOrCtrl+N",
        click: () => send(win, MenuChannels.FILE_NEW),
      },
      {
        label: "&Open...",
        accelerator: "CmdOrCtrl+O",
        click: () => send(win, MenuChannels.FILE_OPEN),
      },
      { type: "separator" },
      {
        label: "&Save",
        accelerator: "CmdOrCtrl+S",
        click: () => send(win, MenuChannels.FILE_SAVE),
      },
      {
        label: "Save &As...",
        accelerator: "CmdOrCtrl+Shift+S",
        click: () => send(win, MenuChannels.FILE_SAVE_AS),
      },
      { type: "separator" },
      {
        label: "E&xit",
        accelerator: process.platform === "darwin" ? undefined : "Alt+F4",
        click: () => {
          send(win, MenuChannels.FILE_EXIT);
          win.close();
        },
      },
    ],
  };
}

function buildEditMenu(win: BrowserWindow): MenuItemConstructorOptions {
  return {
    label: "&Edit",
    submenu: [
      {
        label: "&Undo",
        click: () => send(win, MenuChannels.EDIT_UNDO),
      },
      {
        label: "&Redo",
        click: () => send(win, MenuChannels.EDIT_REDO),
      },
      { type: "separator" },
      {
        label: "&Fill Region...",
        click: () => send(win, MenuChannels.EDIT_FILL_REGION),
      },
      { type: "separator" },
      {
        label: "&Palette",
        submenu: [
          {
            label: "&List Materials",
            click: () => send(win, MenuChannels.EDIT_PALETTE_LIST),
          },
          {
            label: "&Add Material...",
            click: () => send(win, MenuChannels.EDIT_PALETTE_ADD),
          },
        ],
      },
      {
        label: "Re&gions",
        submenu: [
          {
            label: "&List Regions",
            click: () => send(win, MenuChannels.EDIT_REGIONS_LIST),
          },
          {
            label: "La&bel Voxel...",
            click: () => send(win, MenuChannels.EDIT_REGIONS_LABEL),
          },
        ],
      },
      { type: "separator" },
      {
        label: "&Clear All",
        click: () => send(win, MenuChannels.EDIT_CLEAR_ALL),
      },
    ],
  };
}

function buildViewMenu(win: BrowserWindow): MenuItemConstructorOptions {
  return {
    label: "&View",
    submenu: [
      {
        label: "&Front",
        accelerator: "F1",
        click: () => send(win, MenuChannels.VIEW_FRONT),
      },
      {
        label: "&Side",
        accelerator: "F2",
        click: () => send(win, MenuChannels.VIEW_SIDE),
      },
      {
        label: "&Top",
        accelerator: "F3",
        click: () => send(win, MenuChannels.VIEW_TOP),
      },
      { type: "separator" },
      {
        label: "&Wireframe Toggle",
        accelerator: "F4",
        click: () => send(win, MenuChannels.VIEW_WIREFRAME),
      },
      { type: "separator" },
      {
        label: "&Grid Size...",
        click: () => send(win, MenuChannels.VIEW_GRID_SIZE),
      },
      {
        label: "&Measure Grid Toggle",
        accelerator: "F5",
        click: () => send(win, MenuChannels.VIEW_MEASURE_GRID),
      },
      {
        label: "Measure &Scale...",
        click: () => send(win, MenuChannels.VIEW_MEASURE_SCALE),
      },
      { type: "separator" },
      {
        label: "&Background Color...",
        click: () => send(win, MenuChannels.VIEW_BG_COLOR),
      },
    ],
  };
}

/**
 * Reference Model menu — fully enabled with IPC event routing.
 * Each item sends a menu:* IPC event to the renderer which bridges
 * to the Myra CLI via bridge:myra-command-execute → voxelforge.myra.execute.
 */
function buildReferenceMenu(win: BrowserWindow): MenuItemConstructorOptions {
  return {
    label: "&Reference",
    submenu: [
      {
        label: "Load Reference Model...",
        click: () => send(win, MenuChannels.REFERENCE_MODEL_LOAD),
      },
      {
        label: "List Reference Models",
        click: () => send(win, MenuChannels.REFERENCE_MODEL_LIST),
      },
      {
        label: "Remove Reference Model...",
        click: () => send(win, MenuChannels.REFERENCE_MODEL_REMOVE),
      },
      {
        label: "Clear All References",
        click: () => send(win, MenuChannels.REFERENCE_CLEAR),
      },
      { type: "separator" },
      {
        label: "Transform (Position)...",
        click: () => send(win, MenuChannels.REFERENCE_TRANSFORM),
      },
      {
        label: "Render Mode...",
        click: () => send(win, MenuChannels.REFERENCE_MODE),
      },
      {
        label: "Toggle Visibility...",
        click: () => send(win, MenuChannels.REFERENCE_VISIBILITY),
      },
      {
        label: "Scale...",
        click: () => send(win, MenuChannels.REFERENCE_SCALE),
      },
      {
        label: "Rotate...",
        click: () => send(win, MenuChannels.REFERENCE_ROTATE),
      },
      {
        label: "Auto-Orient",
        click: () => send(win, MenuChannels.REFERENCE_ORIENT),
      },
      {
        label: "Info / Inspect...",
        click: () => send(win, MenuChannels.REFERENCE_INFO),
      },
      { type: "separator" },
      {
        label: "Texture Assignment...",
        click: () => send(win, MenuChannels.REFERENCE_TEXTURE_ASSIGN),
      },
      {
        label: "Emissive Texture...",
        click: () => send(win, MenuChannels.REFERENCE_EMISSIVE_ASSIGN),
      },
      { type: "separator" },
      {
        label: "Animation",
        submenu: [
          {
            label: "List Clips",
            click: () => send(win, MenuChannels.REFERENCE_ANIMATION, { action: "list" }),
          },
          {
            label: "Play",
            click: () => send(win, MenuChannels.REFERENCE_ANIMATION, { action: "play" }),
          },
          {
            label: "Stop",
            click: () => send(win, MenuChannels.REFERENCE_ANIMATION, { action: "stop" }),
          },
          {
            label: "Pause",
            click: () => send(win, MenuChannels.REFERENCE_ANIMATION, { action: "pause" }),
          },
        ],
      },
      { type: "separator" },
      {
        label: "Save Meta...",
        click: () => send(win, MenuChannels.REFERENCE_META_SAVE),
      },
      {
        label: "Load Meta...",
        click: () => send(win, MenuChannels.REFERENCE_META_LOAD),
      },
      { type: "separator" },
      {
        label: "Image Ref Load...",
        click: () => send(win, MenuChannels.IMAGE_REF_LOAD),
      },
      {
        label: "Image Ref List",
        click: () => send(win, MenuChannels.IMAGE_REF_LIST),
      },
      {
        label: "Image Ref Remove...",
        click: () => send(win, MenuChannels.IMAGE_REF_REMOVE),
      },
    ],
  };
}

/**
 * Tools menu — advanced baking, palette ops, and screenshot items open the
 * command palette pre-filled with the specific command.
 * Voxelize and Voxelize Compare remain as direct menu items.
 */
function buildToolsMenu(win: BrowserWindow): MenuItemConstructorOptions {
  return {
    label: "&Tools",
    submenu: [
      {
        label: "Voxelize...",
        click: () => send(win, MenuChannels.VOXELIZE_EXECUTE),
      },
      {
        label: "Voxelize & Compare...",
        click: () => send(win, MenuChannels.VOXELIZE_COMPARE),
      },
      { type: "separator" },
      {
        label: "Command Palette...",
        accelerator: "CmdOrCtrl+Shift+P",
        click: () => send(win, MenuChannels.COMMAND_PALETTE, { command: "" }),
      },
      { type: "separator" },
      {
        label: "Bake AO",
        click: () => send(win, MenuChannels.COMMAND_PALETTE_AO_BAKE),
      },
      {
        label: "Edge Darken",
        click: () => send(win, MenuChannels.COMMAND_PALETTE_EDGE_DARKEN),
      },
      {
        label: "Light Bake",
        click: () => send(win, MenuChannels.COMMAND_PALETTE_LIGHT_BAKE),
      },
      {
        label: "Palette Map...",
        click: () => send(win, MenuChannels.COMMAND_PALETTE_PALETTE_MAP),
      },
      {
        label: "Palette Reduce...",
        click: () => send(win, MenuChannels.COMMAND_PALETTE_PALETTE_REDUCE),
      },
      { type: "separator" },
      {
        label: "Screenshot...",
        click: () => send(win, MenuChannels.COMMAND_PALETTE_SCREENSHOT),
      },
    ],
  };
}

function buildHelpMenu(win: BrowserWindow): MenuItemConstructorOptions {
  return {
    label: "&Help",
    submenu: [
      {
        label: "&About VoxelForge",
        click: () => send(win, MenuChannels.HELP_ABOUT),
      },
    ],
  };
}
