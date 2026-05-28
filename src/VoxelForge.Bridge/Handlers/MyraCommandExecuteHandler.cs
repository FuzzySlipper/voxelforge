using System.Text.Json;
using Den.Bridge.Abstractions;
using Den.Bridge.Protocol;
using Microsoft.Extensions.Logging;
using VoxelForge.App.Console;
using VoxelForge.App.Console.Commands;
using VoxelForge.App.Events;
using VoxelForge.App.Reference;
using VoxelForge.App.Services;
using VoxelForge.Bridge.Protocol;
using VoxelForge.Content;

namespace VoxelForge.Bridge.Handlers;

/// <summary>
/// Routes reference-model, image-reference, and voxelize commands through
/// the Myra CLI CommandRouter. Accepts a payload with:
/// { command: string, args: string[] }
/// where 'command' is the Myra CLI command name (e.g. "refload", "imglist")
/// and 'args' are the string arguments.
/// </summary>
public sealed class MyraCommandExecuteHandler
    : IBridgeCommandHandler<MyraCommandExecuteRequest, MyraCommandExecuteResponse>
{
    private readonly ILogger<MyraCommandExecuteHandler> _logger;
    private readonly VoxelModelHolder _modelHolder;
    private readonly EditorUiStateBridgeService _stateService;
    private readonly ReferenceAssetService _referenceAssetService;
    private readonly ReferenceModelLoader _referenceModelLoader;
    private readonly IEventPublisher _events;
    private readonly ILoggerFactory _loggerFactory;

    private CommandRouter? _cachedRouter;

    public MyraCommandExecuteHandler(
        ILogger<MyraCommandExecuteHandler> logger,
        VoxelModelHolder modelHolder,
        EditorUiStateBridgeService stateService,
        ReferenceAssetService referenceAssetService,
        ReferenceModelLoader referenceModelLoader,
        IEventPublisher events,
        ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _modelHolder = modelHolder;
        _stateService = stateService;
        _referenceAssetService = referenceAssetService;
        _referenceModelLoader = referenceModelLoader;
        _events = events;
        _loggerFactory = loggerFactory;
    }

    public ValueTask<MyraCommandExecuteResponse?> HandleAsync(
        MyraCommandExecuteRequest request,
        BridgeRequestContext context,
        CancellationToken cancellationToken)
    {
        var commandName = request.Command?.Trim();
        if (string.IsNullOrEmpty(commandName))
        {
            throw new BridgeHandlerException(
                "voxelforge.myra.missing_command",
                "Myra command execution requires a 'command' string.",
                BridgeErrorCategories.Validation,
                retryable: false);
        }

        var router = GetOrBuildRouter();
        var commandArgs = request.Args ?? [];

        // Build the CommandContext from the current model holder state
        var cmdContext = new CommandContext
        {
            Document = _modelHolder.Document,
            UndoStack = _modelHolder.UndoStack,
            Events = _events,
            Mode = ExecutionMode.Headless,
        };

        _logger.LogDebug("Executing Myra command: {Command} ({ArgCount} args)", commandName, commandArgs.Length);

        var result = router.Execute(commandName, commandArgs, cmdContext);

        return ValueTask.FromResult<MyraCommandExecuteResponse?>(new MyraCommandExecuteResponse
        {
            Success = result.Success,
            Message = result.Message,
            State = _stateService.BuildSnapshot(),
        });
    }

    private CommandRouter GetOrBuildRouter()
    {
        if (_cachedRouter is not null)
            return _cachedRouter;

        // Build the CommandRouter with the reference/image/voxelize commands
        // and other CLI commands that the Electron menu needs to route through.
        // Uses the same services created in VoxelModelHolder.
        var commands = new List<IConsoleCommand>
        {
            // Reference model commands
            new RefLoadCommand(_modelHolder.Workspace.ReferenceModels, _referenceAssetService),
            new RefListCommand(_modelHolder.Workspace.ReferenceModels, _referenceAssetService),
            new RefRemoveCommand(_modelHolder.Workspace.ReferenceModels, _referenceAssetService),
            new RefClearCommand(_modelHolder.Workspace.ReferenceModels, _referenceAssetService),
            new RefTransformCommand(_modelHolder.Workspace.ReferenceModels),
            new RefModeCommand(_modelHolder.Workspace.ReferenceModels),
            new RefVisibilityCommand(_modelHolder.Workspace.ReferenceModels, show: true),
            new RefVisibilityCommand(_modelHolder.Workspace.ReferenceModels, show: false),
            new RefScaleCommand(_modelHolder.Workspace.ReferenceModels),
            new RefRotateCommand(_modelHolder.Workspace.ReferenceModels),
            new RefOrientCommand(_modelHolder.Workspace.ReferenceModels),
            new RefInfoCommand(_modelHolder.Workspace.ReferenceModels, _referenceModelLoader),
            new RefAnimCommand(_modelHolder.Workspace.ReferenceModels),
            new RefTexCommand(_modelHolder.Workspace.ReferenceModels, _referenceModelLoader),
            new RefTexEmissiveCommand(_modelHolder.Workspace.ReferenceModels, _referenceModelLoader),
            new RefSaveMetaCommand(_modelHolder.Workspace.ReferenceModels),
            new RefLoadMetaCommand(_modelHolder.Workspace.ReferenceModels, _referenceModelLoader),

            // Image reference commands
            new ImgLoadCommand(_modelHolder.Workspace.ReferenceImages, _referenceAssetService),
            new ImgListCommand(_modelHolder.Workspace.ReferenceImages, _referenceAssetService),
            new ImgRemoveCommand(_modelHolder.Workspace.ReferenceImages, _referenceAssetService),

            // Voxelize commands
            new VoxelizeCommand(_modelHolder.Workspace.ReferenceModels, _loggerFactory),
            new VoxelizeCompareCommand(_modelHolder.Workspace.ReferenceModels, _loggerFactory),
        };

        _cachedRouter = new CommandRouter(commands, _loggerFactory.CreateLogger<CommandRouter>());
        return _cachedRouter;
    }
}

// ── Request / Response types for the Myra bridge command ──

/// <summary>
/// Request payload for voxelforge.myra.execute.
/// Contains the Myra CLI command name and its string arguments.
/// </summary>
public sealed class MyraCommandExecuteRequest
{
    /// <summary>
    /// Myra CLI command name (e.g. "refload", "refanim", "voxelize").
    /// </summary>
    public string? Command { get; set; }

    /// <summary>
    /// Command arguments as a string array, matching the Myra CLI conventions.
    /// </summary>
    public string[]? Args { get; set; }
}

/// <summary>
/// Response payload for voxelforge.myra.execute.
/// </summary>
public sealed class MyraCommandExecuteResponse
{
    /// <summary>
    /// Whether the command executed successfully.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Human-readable result message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Post-command editor state snapshot.
    /// </summary>
    public required EditorUiStateSnapshot State { get; init; }
}
