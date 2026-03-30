using Myra.Graphics2D.UI;
using Myra.Graphics2D.UI.File;

namespace VoxelForge.Engine.MonoGame.UI;

/// <summary>
/// Builds the top-level HorizontalMenu for the editor.
/// Each menu item dispatches through the shared command system so GUI/console/LLM stay in sync.
/// </summary>
public sealed class EditorMenuBar
{
    public HorizontalMenu Menu { get; }

    private readonly MenuCommandDispatcher _dispatcher;
    private Desktop? _desktop;
    private Action? _exitAction;

    /// <summary>
    /// Current project file path. Null means unsaved/new project.
    /// </summary>
    public string? CurrentProjectPath { get; set; }

    // View menu actions — wired by VoxelForgeGame after construction
    public Action? OnSnapFront { get; set; }
    public Action? OnSnapSide { get; set; }
    public Action? OnSnapTop { get; set; }
    public Action? OnToggleWireframe { get; set; }

    public EditorMenuBar(MenuCommandDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        Menu = new HorizontalMenu();

        Menu.Items.Add(BuildFileMenu());
        Menu.Items.Add(BuildEditMenu());
        Menu.Items.Add(BuildViewMenu());
        Menu.Items.Add(BuildReferenceMenu());
        Menu.Items.Add(BuildToolsMenu());
        Menu.Items.Add(BuildHelpMenu());
    }

    /// <summary>
    /// Must be called after the Desktop is created so dialogs can be shown.
    /// </summary>
    public void Initialize(Desktop desktop, Action exitAction)
    {
        _desktop = desktop;
        _exitAction = exitAction;
    }

    private MenuItem BuildFileMenu()
    {
        var menu = new MenuItem("file", "&File");

        // New — confirm then clear
        var newItem = new MenuItem("file_new", "&New");
        newItem.ShortcutText = "Ctrl+N";
        newItem.Selected += (_, _) => ShowConfirmDialog(
            "New Project",
            "Clear the current model? Unsaved changes will be lost.",
            () =>
            {
                _dispatcher.Dispatch("clear");
                CurrentProjectPath = null;
            });
        menu.Items.Add(newItem);

        menu.Items.Add(new MenuSeparator());

        // Open — file dialog
        var open = new MenuItem("file_open", "&Open...");
        open.ShortcutText = "Ctrl+O";
        open.Selected += (_, _) => ShowOpenDialog();
        menu.Items.Add(open);

        // Save — save to current path, or prompt Save As
        var save = new MenuItem("file_save", "&Save");
        save.ShortcutText = "Ctrl+S";
        save.Selected += (_, _) =>
        {
            if (CurrentProjectPath is not null)
                _dispatcher.Dispatch("save", CurrentProjectPath);
            else
                ShowSaveAsDialog();
        };
        menu.Items.Add(save);

        // Save As — file dialog
        var saveAs = new MenuItem("file_saveas", "Save &As...");
        saveAs.ShortcutText = "Ctrl+Shift+S";
        saveAs.Selected += (_, _) => ShowSaveAsDialog();
        menu.Items.Add(saveAs);

        menu.Items.Add(new MenuSeparator());

        var list = Cmd("file_list", "List &Files", "list");
        menu.Items.Add(list);

        menu.Items.Add(new MenuSeparator());

        var exit = new MenuItem("file_exit", "E&xit");
        exit.Selected += (_, _) => _exitAction?.Invoke();
        menu.Items.Add(exit);

        return menu;
    }

    private void ShowOpenDialog()
    {
        if (_desktop is null) return;

        var dlg = new FileDialog(FileDialogMode.OpenFile)
        {
            Filter = "*.vforge",
            Folder = Path.GetFullPath("content"),
        };

        dlg.Closed += (_, _) =>
        {
            if (dlg.Result && !string.IsNullOrEmpty(dlg.FilePath))
            {
                _dispatcher.Dispatch("load", dlg.FilePath);
                CurrentProjectPath = dlg.FilePath;
            }
        };

        dlg.ShowModal(_desktop);
    }

    private void ShowSaveAsDialog()
    {
        if (_desktop is null) return;

        var dlg = new FileDialog(FileDialogMode.SaveFile)
        {
            Filter = "*.vforge",
            Folder = Path.GetFullPath("content"),
        };

        dlg.Closed += (_, _) =>
        {
            if (dlg.Result && !string.IsNullOrEmpty(dlg.FilePath))
            {
                var path = dlg.FilePath;
                if (!path.EndsWith(".vforge", StringComparison.OrdinalIgnoreCase))
                    path += ".vforge";

                _dispatcher.Dispatch("save", path);
                CurrentProjectPath = path;
            }
        };

        dlg.ShowModal(_desktop);
    }

    private void ShowConfirmDialog(string title, string message, Action onConfirm)
    {
        if (_desktop is null)
        {
            onConfirm();
            return;
        }

        var dlg = new Dialog
        {
            Title = title,
            Content = new Label { Text = message, Wrap = true, Width = 300 },
        };

        dlg.Closed += (_, _) =>
        {
            if (dlg.Result == true)
                onConfirm();
        };

        dlg.ShowModal(_desktop);
    }

    private MenuItem BuildEditMenu()
    {
        var menu = new MenuItem("edit", "&Edit");

        var undo = Cmd("edit_undo", "&Undo", "undo");
        undo.ShortcutText = "Ctrl+Z";
        menu.Items.Add(undo);

        var redo = Cmd("edit_redo", "&Redo", "redo");
        redo.ShortcutText = "Ctrl+Y";
        menu.Items.Add(redo);

        menu.Items.Add(new MenuSeparator());

        var fill = new MenuItem("edit_fill", "&Fill Region...");
        fill.Selected += (_, _) => ShowArgsDialog("Fill Region",
            ["X1", "Y1", "Z1", "X2", "Y2", "Z2", "Palette Index"],
            args => _dispatcher.Dispatch("fill", args));
        menu.Items.Add(fill);

        menu.Items.Add(new MenuSeparator());

        // Palette submenu
        var palette = new MenuItem("edit_palette", "&Palette");

        var palList = Cmd("edit_pal_list", "&List Materials", "palette");
        palette.Items.Add(palList);

        var palAdd = new MenuItem("edit_pal_add", "&Add Material...");
        palAdd.Selected += (_, _) => ShowArgsDialog("Add Palette Material",
            ["Index", "Name", "R (0-255)", "G (0-255)", "B (0-255)", "A (0-255, default 255)"],
            args =>
            {
                var cmdArgs = new List<string> { "add" };
                cmdArgs.AddRange(args.Where(a => a != ""));
                _dispatcher.Dispatch("palette", cmdArgs.ToArray());
            });
        palette.Items.Add(palAdd);

        menu.Items.Add(palette);

        // Regions submenu
        var regions = new MenuItem("edit_regions", "Re&gions");

        var regList = Cmd("edit_reg_list", "&List Regions", "regions");
        regions.Items.Add(regList);

        var regLabel = new MenuItem("edit_reg_label", "La&bel Voxel...");
        regLabel.Selected += (_, _) => ShowArgsDialog("Label Voxel",
            ["Region Name", "X", "Y", "Z"],
            args => _dispatcher.Dispatch("label", args));
        regions.Items.Add(regLabel);

        menu.Items.Add(regions);

        menu.Items.Add(new MenuSeparator());

        var clear = new MenuItem("edit_clear", "&Clear All");
        clear.Selected += (_, _) => ShowConfirmDialog(
            "Clear All",
            "Remove all voxels from the model?",
            () => _dispatcher.Dispatch("clear"));
        menu.Items.Add(clear);

        return menu;
    }

    private MenuItem BuildViewMenu()
    {
        var menu = new MenuItem("view", "&View");

        var front = new MenuItem("view_front", "&Front");
        front.ShortcutText = "F1";
        front.Selected += (_, _) => OnSnapFront?.Invoke();
        menu.Items.Add(front);

        var side = new MenuItem("view_side", "&Side");
        side.ShortcutText = "F2";
        side.Selected += (_, _) => OnSnapSide?.Invoke();
        menu.Items.Add(side);

        var top = new MenuItem("view_top", "&Top");
        top.ShortcutText = "F3";
        top.Selected += (_, _) => OnSnapTop?.Invoke();
        menu.Items.Add(top);

        menu.Items.Add(new MenuSeparator());

        var wireframe = new MenuItem("view_wireframe", "&Wireframe Toggle");
        wireframe.ShortcutText = "F4";
        wireframe.Selected += (_, _) => OnToggleWireframe?.Invoke();
        menu.Items.Add(wireframe);

        menu.Items.Add(new MenuSeparator());

        var grid = new MenuItem("view_grid", "&Grid Size...");
        grid.Selected += (_, _) => ShowArgsDialog("Grid Size",
            ["Size (1-256)"],
            args => _dispatcher.Dispatch("grid", args));
        menu.Items.Add(grid);

        menu.Items.Add(new MenuSeparator());

        var bgColor = new MenuItem("view_bgcolor", "&Background Color...");
        bgColor.Selected += (_, _) => ShowArgsDialog("Background Color",
            ["R (0-255)", "G (0-255)", "B (0-255)"],
            args =>
            {
                if (args.Length == 3)
                {
                    _dispatcher.Dispatch("config", "backgroundcolor", $"{args[0]},{args[1]},{args[2]}");
                    _dispatcher.Dispatch("config", "save");
                }
            });
        menu.Items.Add(bgColor);

        return menu;
    }

    /// <summary>
    /// Shows a dialog with labeled text fields, then calls onSubmit with the values.
    /// Reusable for any command that needs argument input.
    /// </summary>
    private void ShowArgsDialog(string title, string[] fieldLabels, Action<string[]> onSubmit)
    {
        if (_desktop is null) return;

        var fields = new TextBox[fieldLabels.Length];
        var panel = new VerticalStackPanel { Spacing = 4 };

        for (int i = 0; i < fieldLabels.Length; i++)
        {
            var row = new HorizontalStackPanel { Spacing = 8 };
            row.Widgets.Add(new Label { Text = fieldLabels[i], Width = 120 });
            fields[i] = new TextBox { Width = 100 };
            row.Widgets.Add(fields[i]);
            panel.Widgets.Add(row);
        }

        var dlg = new Dialog
        {
            Title = title,
            Content = panel,
        };

        dlg.Closed += (_, _) =>
        {
            if (dlg.Result == true)
            {
                var values = new string[fields.Length];
                for (int i = 0; i < fields.Length; i++)
                    values[i] = fields[i].Text?.Trim() ?? "";
                onSubmit(values);
            }
        };

        dlg.ShowModal(_desktop);
    }

    private MenuItem BuildReferenceMenu()
    {
        var menu = new MenuItem("ref", "&Reference");

        // Models submenu
        var models = new MenuItem("ref_models", "&Models");

        var load = new MenuItem("ref_load", "&Load Model...");
        load.Selected += (_, _) => ShowRefLoadDialog();
        models.Items.Add(load);

        var list = Cmd("ref_list", "L&ist Models", "reflist");
        models.Items.Add(list);

        models.Items.Add(new MenuSeparator());

        var orient = new MenuItem("ref_orient", "&Auto-Orient...");
        orient.Selected += (_, _) => ShowArgsDialog("Auto-Orient",
            ["Model Index"],
            args => _dispatcher.Dispatch("reforient", args));
        models.Items.Add(orient);

        var transform = new MenuItem("ref_transform", "&Transform...");
        transform.Selected += (_, _) => ShowArgsDialog("Transform Model",
            ["Index", "X", "Y", "Z", "Rot X", "Rot Y", "Rot Z", "Scale"],
            args => _dispatcher.Dispatch("reftransform", args));
        models.Items.Add(transform);

        var rotate = new MenuItem("ref_rotate", "&Rotate 90...");
        rotate.Selected += (_, _) => ShowArgsDialog("Rotate 90",
            ["Index", "Axis (x/y/z)", "Degrees (default 90)"],
            args => _dispatcher.Dispatch("refrotate", args));
        models.Items.Add(rotate);

        var scale = new MenuItem("ref_scale", "S&cale...");
        scale.Selected += (_, _) => ShowArgsDialog("Scale Model",
            ["Index", "Scale"],
            args => _dispatcher.Dispatch("refscale", args));
        models.Items.Add(scale);

        models.Items.Add(new MenuSeparator());

        var mode = new MenuItem("ref_mode", "Render &Mode...");
        mode.Selected += (_, _) => ShowArgsDialog("Render Mode",
            ["Index", "Mode (solid/wireframe/transparent)"],
            args => _dispatcher.Dispatch("refmode", args));
        models.Items.Add(mode);

        menu.Items.Add(models);

        // Animation submenu
        var anim = new MenuItem("ref_anim", "&Animation");

        var animList = new MenuItem("ref_anim_list", "&List Clips...");
        animList.Selected += (_, _) => ShowArgsDialog("List Animation Clips",
            ["Model Index"],
            args => _dispatcher.Dispatch($"refanim {args[0]} list"));
        anim.Items.Add(animList);

        var animPlay = new MenuItem("ref_anim_play", "&Play...");
        animPlay.Selected += (_, _) => ShowArgsDialog("Play Animation",
            ["Model Index", "Clip (name or index)"],
            args => _dispatcher.Dispatch($"refanim {args[0]} play {args[1]}"));
        anim.Items.Add(animPlay);

        var animStop = new MenuItem("ref_anim_stop", "&Stop...");
        animStop.Selected += (_, _) => ShowArgsDialog("Stop Animation",
            ["Model Index"],
            args => _dispatcher.Dispatch($"refanim {args[0]} stop"));
        anim.Items.Add(animStop);

        var animPause = new MenuItem("ref_anim_pause", "Pa&use...");
        animPause.Selected += (_, _) => ShowArgsDialog("Pause Animation",
            ["Model Index"],
            args => _dispatcher.Dispatch($"refanim {args[0]} pause"));
        anim.Items.Add(animPause);

        menu.Items.Add(anim);

        menu.Items.Add(new MenuSeparator());

        // Images submenu
        var images = new MenuItem("ref_images", "&Images");

        var imgLoad = new MenuItem("ref_img_load", "&Load Image...");
        imgLoad.Selected += (_, _) => ShowImageLoadDialog();
        images.Items.Add(imgLoad);

        var imgList = Cmd("ref_img_list", "L&ist Images", "imglist");
        images.Items.Add(imgList);

        var imgRemove = new MenuItem("ref_img_remove", "&Remove Image...");
        imgRemove.Selected += (_, _) => ShowArgsDialog("Remove Image",
            ["Image Index"],
            args => _dispatcher.Dispatch("imgremove", args));
        images.Items.Add(imgRemove);

        menu.Items.Add(images);

        menu.Items.Add(new MenuSeparator());

        // Voxelize
        var voxelize = new MenuItem("ref_voxelize", "&Voxelize...");
        voxelize.Selected += (_, _) => ShowArgsDialog("Voxelize Model",
            ["Ref Model Index", "Resolution", "Mode (surface/solid)"],
            args => _dispatcher.Dispatch("voxelize", args));
        menu.Items.Add(voxelize);

        return menu;
    }

    private void ShowRefLoadDialog()
    {
        if (_desktop is null) return;

        var dlg = new FileDialog(FileDialogMode.OpenFile)
        {
            Filter = "*.fbx;*.obj;*.gltf;*.glb;*.dae;*.3ds;*.blend",
        };

        dlg.Closed += (_, _) =>
        {
            if (dlg.Result && !string.IsNullOrEmpty(dlg.FilePath))
                _dispatcher.Dispatch("refload", dlg.FilePath);
        };

        dlg.ShowModal(_desktop);
    }

    private void ShowImageLoadDialog()
    {
        if (_desktop is null) return;

        var dlg = new FileDialog(FileDialogMode.OpenFile)
        {
            Filter = "*.png;*.jpg;*.jpeg;*.bmp;*.tga",
        };

        dlg.Closed += (_, _) =>
        {
            if (dlg.Result && !string.IsNullOrEmpty(dlg.FilePath))
                _dispatcher.Dispatch("imgload", dlg.FilePath);
        };

        dlg.ShowModal(_desktop);
    }

    private MenuItem BuildToolsMenu()
    {
        var menu = new MenuItem("tools", "&Tools");

        var screenshot = new MenuItem("tools_screenshot", "&Screenshot");

        var ssViewport = Cmd("tools_ss_viewport", "Capture &Viewport", "screenshot");
        screenshot.Items.Add(ssViewport);

        var ssAll = Cmd("tools_ss_all", "Capture &All Angles", "screenshot all");
        screenshot.Items.Add(ssAll);

        var ssCustom = new MenuItem("tools_ss_custom", "Custom &Angle...");
        ssCustom.Selected += (_, _) => ShowArgsDialog("Screenshot Custom Angle",
            ["Yaw (degrees)", "Pitch (degrees)"],
            args => _dispatcher.Dispatch($"screenshot angle {args[0]} {args[1]}"));
        screenshot.Items.Add(ssCustom);

        menu.Items.Add(screenshot);

        menu.Items.Add(new MenuSeparator());

        var exec = new MenuItem("tools_exec", "&Execute Script...");
        exec.Selected += (_, _) => ShowScriptDialog();
        menu.Items.Add(exec);

        menu.Items.Add(new MenuSeparator());

        // Settings submenu
        var settings = new MenuItem("tools_settings", "S&ettings");

        var camSettings = new MenuItem("tools_cam", "&Camera...");
        camSettings.Selected += (_, _) => ShowArgsDialog("Camera Settings",
            ["Orbit Sensitivity", "Zoom Sensitivity", "Pan Speed", "Invert Orbit X (true/false)", "Invert Orbit Y (true/false)"],
            args =>
            {
                if (args.Length >= 1 && args[0] != "") _dispatcher.Dispatch("config", "orbitsensitivity", args[0]);
                if (args.Length >= 2 && args[1] != "") _dispatcher.Dispatch("config", "zoomsensitivity", args[1]);
                if (args.Length >= 3 && args[2] != "") _dispatcher.Dispatch("config", "panspeed", args[2]);
                if (args.Length >= 4 && args[3] != "") _dispatcher.Dispatch("config", "invertorbitx", args[3]);
                if (args.Length >= 5 && args[4] != "") _dispatcher.Dispatch("config", "invertorbity", args[4]);
                _dispatcher.Dispatch("config save");
            });
        settings.Items.Add(camSettings);

        var displaySettings = new MenuItem("tools_display", "&Display...");
        displaySettings.Selected += (_, _) => ShowArgsDialog("Display Settings",
            ["Default Grid Size", "Max Undo Depth"],
            args =>
            {
                if (args.Length >= 1 && args[0] != "") _dispatcher.Dispatch("config", "defaultgridhint", args[0]);
                if (args.Length >= 2 && args[1] != "") _dispatcher.Dispatch("config", "maxundodepth", args[1]);
                _dispatcher.Dispatch("config save");
            });
        settings.Items.Add(displaySettings);

        settings.Items.Add(new MenuSeparator());

        var saveConfig = Cmd("tools_savecfg", "&Save Settings", "config save");
        settings.Items.Add(saveConfig);

        menu.Items.Add(settings);

        return menu;
    }

    private void ShowScriptDialog()
    {
        if (_desktop is null) return;

        var dlg = new FileDialog(FileDialogMode.OpenFile)
        {
            Filter = "*.txt;*.vfscript",
        };

        dlg.Closed += (_, _) =>
        {
            if (dlg.Result && !string.IsNullOrEmpty(dlg.FilePath))
                _dispatcher.Dispatch("exec", dlg.FilePath);
        };

        dlg.ShowModal(_desktop);
    }

    private MenuItem BuildHelpMenu()
    {
        var menu = new MenuItem("help", "&Help");

        var commands = new MenuItem("help_commands", "&Command Reference");
        commands.Selected += (_, _) => ShowCommandReference();
        menu.Items.Add(commands);

        var describe = new MenuItem("help_describe", "&Model Info");
        describe.Selected += (_, _) => ShowResultDialog("Model Info", _dispatcher.Dispatch("describe"));
        menu.Items.Add(describe);

        menu.Items.Add(new MenuSeparator());

        var about = new MenuItem("help_about", "&About VoxelForge");
        about.Selected += (_, _) => ShowInfoDialog("About VoxelForge",
            "VoxelForge — Voxel Authoring Tool\nwith LLM Integration\n\nCoordinate System: Y-up (right-handed)\nR=X, G=Y, B=Z");
        menu.Items.Add(about);

        return menu;
    }

    private void ShowCommandReference()
    {
        var result = _dispatcher.Dispatch("help");
        ShowResultDialog("Command Reference", result);
    }

    private void ShowResultDialog(string title, VoxelForge.App.Console.CommandResult result)
    {
        if (_desktop is null) return;

        var dlg = new Dialog
        {
            Title = title,
            Content = new ScrollViewer
            {
                Content = new Label { Text = result.Message, Wrap = true, Width = 400 },
                MaxHeight = 400,
                ShowHorizontalScrollBar = false,
            },
        };
        dlg.ButtonCancel.Visible = false;
        dlg.ShowModal(_desktop);
    }

    private void ShowInfoDialog(string title, string message)
    {
        if (_desktop is null) return;

        var dlg = new Dialog
        {
            Title = title,
            Content = new Label { Text = message, Wrap = true, Width = 300 },
        };
        dlg.ButtonCancel.Visible = false;
        dlg.ShowModal(_desktop);
    }

    /// <summary>
    /// Create a menu item that directly dispatches a command string on click.
    /// </summary>
    private MenuItem Cmd(string id, string text, string commandString)
    {
        var item = new MenuItem(id, text);
        item.Selected += (_, _) => _dispatcher.Dispatch(commandString);
        return item;
    }
}
