using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.App;
using VoxelForge.App.Commands;
using VoxelForge.App.Console;
using VoxelForge.Core;
using VoxelForge.Engine.MonoGame;

var headless = args.Contains("--headless");
var loggerFactory = NullLoggerFactory.Instance;

// Create shared state
var model = new VoxelModel(loggerFactory.CreateLogger<VoxelModel>()) { GridHint = 16 };
var labels = new LabelIndex(loggerFactory.CreateLogger<LabelIndex>());
var editorState = new EditorState(model, labels, loggerFactory.CreateLogger<EditorState>());
var undoStack = new UndoStack(100, loggerFactory.CreateLogger<UndoStack>());

// Build console
var router = CommandRegistry.Build(loggerFactory);
var context = new CommandContext
{
    Model = model,
    Labels = labels,
    Clips = editorState.Clips,
    UndoStack = undoStack,
};

// Set up demo content
SetupDemoModel(model);

using var cts = new CancellationTokenSource();

if (headless)
{
    // Headless mode: stdio or interactive console, no window
    Console.Error.WriteLine("VoxelForge headless mode");

    bool isPiped = !Console.IsInputRedirected is false || Console.IsInputRedirected;
    if (Console.IsInputRedirected)
    {
        var stdio = new StdioHost(router, context);
        stdio.Run(cts.Token);
    }
    else
    {
        var console = new InteractiveConsoleHost(router, context);
        console.Run(cts.Token);
    }
}
else
{
    // GUI mode: MonoGame window + terminal console on background thread
    context.OnModelChanged = () => editorState.NotifyModelChanged();

    var consoleThread = new Thread(() =>
    {
        // Detect piped stdin → use stdio JSON protocol, otherwise interactive
        if (Console.IsInputRedirected)
        {
            var stdio = new StdioHost(router, context);
            stdio.Run(cts.Token);
        }
        else
        {
            var console = new InteractiveConsoleHost(router, context);
            console.Run(cts.Token);
        }
        // Console exit → signal game to close
        cts.Cancel();
    })
    {
        IsBackground = true,
        Name = "VoxelForge Console",
    };
    consoleThread.Start();

    using var game = new VoxelForgeGame(editorState, undoStack, cts);
    game.Run();
}

static void SetupDemoModel(VoxelModel model)
{
    model.Palette.Set(1, new MaterialDef { Name = "Stone", Color = new RgbaColor(160, 160, 160) });
    model.Palette.Set(2, new MaterialDef { Name = "Grass", Color = new RgbaColor(80, 160, 60) });
    model.Palette.Set(3, new MaterialDef { Name = "Wood", Color = new RgbaColor(139, 90, 43) });

    model.FillRegion(new Point3(0, 0, 0), new Point3(15, 0, 15), 2);
    model.FillRegion(new Point3(0, 1, 0), new Point3(15, 6, 0), 1);
    model.FillRegion(new Point3(0, 1, 15), new Point3(15, 6, 15), 1);
    model.FillRegion(new Point3(0, 1, 0), new Point3(0, 6, 15), 1);
    model.FillRegion(new Point3(15, 1, 0), new Point3(15, 6, 15), 1);

    for (int i = 0; i <= 8; i++)
        model.FillRegion(new Point3(i, 7 + i, i), new Point3(15 - i, 7 + i, 15 - i), 3);
}
