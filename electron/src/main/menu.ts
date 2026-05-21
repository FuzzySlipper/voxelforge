import { app, Menu, MenuItemConstructorOptions, BrowserWindow } from "electron";
import { MenuChannels } from "../shared/menu-channels";

/**
 * Build and set the native Electron application menu matching the Myra editor layout.
 * Menu clicks send IPC events to the renderer, which wires them to existing bridge commands
 * (runAction / executeCommand) and may show dialogs natively in the renderer.
 */
export function setupMenu(mainWindow: BrowserWindow): void {
  const template: MenuItemConstructorOptions[] = [
    buildFileMenu(mainWindow),
    buildEditMenu(mainWindow),
    buildViewMenu(mainWindow),
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
        accelerator: process.platform === "darwin" ? "Cmd+Q" : "Alt+F4",
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
        accelerator: "CmdOrCtrl+Z",
        click: () => send(win, MenuChannels.EDIT_UNDO),
      },
      {
        label: "&Redo",
        accelerator: "CmdOrCtrl+Y",
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
