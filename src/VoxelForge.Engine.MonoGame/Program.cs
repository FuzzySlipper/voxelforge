using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.App;
using VoxelForge.App.Commands;
using VoxelForge.App.Console;
using VoxelForge.App.Reference;
using VoxelForge.Content;
using VoxelForge.Core;
using VoxelForge.Engine.MonoGame;

var headless = args.Contains("--headless");
var loggerFactory = NullLoggerFactory.Instance;

// Load config
var config = EditorConfig.Load();

// Create shared state
var model = new VoxelModel(NullLogger<VoxelModel>.Instance) { GridHint = config.DefaultGridHint };
var labels = new LabelIndex(NullLogger<LabelIndex>.Instance);
var editorState = new EditorState(model, labels, NullLogger<EditorState>.Instance);
var undoStack = new UndoStack(config.MaxUndoDepth, NullLogger<UndoStack>.Instance);
var refRegistry = new ReferenceModelRegistry();
var refLoader = new ReferenceModelLoader(NullLogger<ReferenceModelLoader>.Instance);
var imageStore = new ReferenceImageStore();

// Game reference (set after construction, screenshot provider available after LoadContent)
VoxelForgeGame? game = null;

// Build console — screenshot provider resolves lazily from the game
var router = CommandRegistry.Build(loggerFactory, config, refRegistry, refLoader, imageStore,
    screenshotFactory: () => game?.ScreenshotProvider);
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
    Console.Error.WriteLine("VoxelForge headless mode");

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
    context.OnModelChanged = () => editorState.NotifyModelChanged();

    game = new VoxelForgeGame(editorState, undoStack, config, refRegistry, imageStore, cts);

    var consoleThread = new Thread(() =>
    {
        // Wait for game to finish GPU initialization
        game.Ready.Wait(cts.Token);

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
        cts.Cancel();
    })
    {
        IsBackground = true,
        Name = "VoxelForge Console",
    };
    consoleThread.Start();

    using (game)
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
