using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.App;
using VoxelForge.App.Commands;
using VoxelForge.App.Console;
using VoxelForge.App.Events;
using VoxelForge.App.LivePreview;
using VoxelForge.App.Reference;
using VoxelForge.App.Services;
using VoxelForge.Content;
using VoxelForge.Core;
using VoxelForge.Engine.MonoGame;

var headless = args.Contains("--headless");
var watchPath = GetOptionValue(args, "--watch") ?? GetOptionValue(args, "--preview-watch");
var loggerFactory = NullLoggerFactory.Instance;

// Create durable state first, then wire services and adapters around it.
var configState = EditorConfigState.Load();
var model = new VoxelModel(NullLogger<VoxelModel>.Instance) { GridHint = configState.DefaultGridHint };
var labels = new LabelIndex(NullLogger<LabelIndex>.Instance);
var documentState = new EditorDocumentState(model, labels);
var sessionState = new EditorSessionState();
var undoHistoryState = new UndoHistoryState(configState.MaxUndoDepth);
var events = new ApplicationEventDispatcher();
var referenceModelState = new ReferenceModelState();
var referenceImageState = new ReferenceImageState();

var editorState = new EditorState(documentState, sessionState);
var undoHistoryService = new UndoHistoryService(NullLogger<UndoHistoryService>.Instance);
var undoStack = new UndoStack(undoHistoryState, undoHistoryService, events);
var refLoader = new ReferenceModelLoader(NullLogger<ReferenceModelLoader>.Instance);
ProjectFileWatcher? projectWatchService = null;
if (!string.IsNullOrWhiteSpace(watchPath))
{
    projectWatchService = new ProjectFileWatcher(
        new ProjectLifecycleService(loggerFactory),
        documentState,
        undoStack,
        events,
        watchPath,
        loggerFactory.CreateLogger<ProjectFileWatcher>());
}

// Game reference (set after construction, screenshot provider available after LoadContent)
VoxelForgeGame? game = null;

// Build console — screenshot provider resolves lazily from the game
var router = CommandRegistry.Build(loggerFactory, configState, referenceModelState, refLoader, referenceImageState,
    screenshotFactory: () => game?.ScreenshotProvider);
var context = new CommandContext
{
    Document = documentState,
    UndoStack = undoStack,
    Events = events,
};

using var cts = new CancellationTokenSource();

if (headless)
{
    Console.Error.WriteLine("VoxelForge headless mode");
    if (projectWatchService is not null)
        Console.Error.WriteLine("Project watch mode requires the GUI renderer and is ignored in headless mode.");

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
    game = new VoxelForgeGame(editorState, undoStack, configState, referenceModelState, referenceImageState,
        events, cts, router, context, projectWatchService);

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

static string? GetOptionValue(string[] arguments, string optionName)
{
    for (int i = 0; i < arguments.Length; i++)
    {
        string argument = arguments[i];
        if (string.Equals(argument, optionName, StringComparison.Ordinal))
        {
            if (i + 1 < arguments.Length)
                return arguments[i + 1];

            return null;
        }

        string prefix = optionName + "=";
        if (argument.StartsWith(prefix, StringComparison.Ordinal))
            return argument[prefix.Length..];
    }

    return null;
}
