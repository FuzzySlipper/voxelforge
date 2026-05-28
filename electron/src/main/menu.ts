import { app, Menu, MenuItemConstructorOptions, BrowserWindow } from "electron";
import { MenuChannels } from "../shared/menu-channels";

/**
 * Build and set the native Electron application menu for the canonical JS renderer shell.
 * Menu clicks send IPC events to the renderer, which wires them to existing bridge commands
 * (runAction / executeCommand) and may show dialogs natively in the renderer.
 *
 * Extended workflows (reference model, texture, animation, image_ref, voxelize) are shown as
 * disabled placeholder items with human-readable follow-up context. They appear in the menu
 * as "unavailable" to give visibility into the Myra CLI parity gap, with the bridge command
 * name included in the tooltip/context so a future coder can wire them properly.
 *
 * Den follow-up tracking:
 *   - Reference model workflow → follow-up bridge handler registration
 *   - Texture/Emissive assignments → follow-up bridge handler registration
 *   - Animation commands → follow-up bridge handler registration
 *   - Image reference → follow-up bridge handler registration
 *   - Voxelize → follow-up bridge handler registration
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
 * Reference Model menu — shows all Myra reference_model.* commands as disabled
 * placeholder items. These are bridge:command-execute based and require a
 * command-palette/console panel or dedicated handler wiring in a follow-up task.
 *
 * Reminder: Reference model CLI commands registered in CommandRegistry.cs:
 *   RefLoadCommand, RefListCommand, RefRemoveCommand, RefClearCommand,
 *   RefTransformCommand, RefModeCommand, RefVisibilityCommand,
 *   RefScaleCommand, RefRotateCommand, RefOrientCommand, RefInfoCommand,
 *   RefAnimCommand, RefTexCommand, RefTexEmissiveCommand,
 *   RefSaveMetaCommand, RefLoadMetaCommand
 *
 * These are accessible via C# Myra CLI but need Electron bridge handler wiring
 * (follow-up bridge handler registration).
 */
function buildReferenceMenu(win: BrowserWindow): MenuItemConstructorOptions {
  return {
    label: "&Reference",
    submenu: [
      {
        label: "Load Reference Model",
        enabled: false,
        toolTip: "reference_model.load — bridge handler follow-up needed",
      },
      {
        label: "List Reference Models",
        enabled: false,
        toolTip: "reference_model.list — bridge handler follow-up needed",
      },
      {
        label: "Remove Reference Model",
        enabled: false,
        toolTip: "reference_model.remove — bridge handler follow-up needed",
      },
      {
        label: "Clear All References",
        enabled: false,
        toolTip: "reference_model.clear — bridge handler follow-up needed",
      },
      { type: "separator" },
      {
        label: "Transform (Orient/Rotate/Scale/Mode)",
        enabled: false,
        toolTip: "transform.* — bridge handlers follow-up needed",
      },
      {
        label: "Texture Assignment",
        enabled: false,
        toolTip: "texture.assign / emissive.assign — bridge handlers follow-up needed",
      },
      {
        label: "Animation",
        enabled: false,
        toolTip: "animation.list / animation.play — bridge handlers follow-up needed",
      },
      {
        label: "Save/Load Meta",
        enabled: false,
        toolTip: "meta.save / meta.load — bridge handlers follow-up needed",
      },
      { type: "separator" },
      {
        label: "Image Ref Load",
        enabled: false,
        toolTip: "image_ref.load — bridge handler follow-up needed",
      },
      {
        label: "Image Ref List",
        enabled: false,
        toolTip: "image_ref.list — bridge handler follow-up needed",
      },
      {
        label: "Image Ref Remove",
        enabled: false,
        toolTip: "image_ref.remove — bridge handler follow-up needed",
      },
    ],
  };
}

/**
 * Tools menu — advanced baking, voxelization, screenshot operations.
 * These are accessible via the Myra CLI (bridge:command-execute) but need
 * proper dialog/handler wiring.
 */
function buildToolsMenu(win: BrowserWindow): MenuItemConstructorOptions {
  return {
    label: "&Tools",
    submenu: [
      {
        label: "Bake AO",
        enabled: false,
        toolTip: "ao_bake — bridge handler follow-up needed",
      },
      {
        label: "Edge Darken",
        enabled: false,
        toolTip: "edge_darken — bridge handler follow-up needed",
      },
      {
        label: "Light Bake",
        enabled: false,
        toolTip: "light_bake — bridge handler follow-up needed",
      },
      {
        label: "Palette Map",
        enabled: false,
        toolTip: "palette_map — bridge handler follow-up needed",
      },
      {
        label: "Palette Reduce",
        enabled: false,
        toolTip: "palette_reduce — bridge handler follow-up needed",
      },
      { type: "separator" },
      {
        label: "Voxelize",
        enabled: false,
        toolTip: "voxelize.execute — bridge handler follow-up needed",
      },
      {
        label: "Voxelize & Compare",
        enabled: false,
        toolTip: "voxelize.compare — bridge handler follow-up needed",
      },
      { type: "separator" },
      {
        label: "Screenshot",
        enabled: false,
        toolTip: "screenshot — bridge handler follow-up needed",
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
