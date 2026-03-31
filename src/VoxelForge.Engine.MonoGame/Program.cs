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

using var cts = new CancellationTokenSource();

if (headless)
{
    Console.Error.WriteLine("VoxelForge headless mode");

    if (Console.IsInputRedirected)
    {
        context.Mode = ExecutionMode.Stdio;
        var stdio = new StdioHost(router, context);
        stdio.Run(cts.Token);
    }
    else
    {
        context.Mode = ExecutionMode.Headless;
        var console = new InteractiveConsoleHost(router, context);
        console.Run(cts.Token);
    }
}
else
{
    context.OnModelChanged = () => editorState.NotifyModelChanged();

    game = new VoxelForgeGame(editorState, undoStack, config, refRegistry, imageStore, cts, router, context);

    var consoleThread = new Thread(() =>
    {
        // Wait for game to finish GPU initialization
        game.Ready.Wait(cts.Token);

        if (Console.IsInputRedirected)
        {
            context.Mode = ExecutionMode.Stdio;
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
