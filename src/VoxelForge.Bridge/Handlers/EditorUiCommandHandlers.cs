using System.Text.Json;
using Den.Bridge.Abstractions;
using Den.Bridge.Protocol;
using VoxelForge.App;
using VoxelForge.App.Services;
using VoxelForge.Bridge.Protocol;

namespace VoxelForge.Bridge.Handlers;

public sealed class EditorStateSubscribeHandler : IBridgeCommandHandler<EditorStateSubscribeRequest, EditorStateSubscribeResponse>
{
    private readonly EditorUiStateBridgeService _stateService;

    public EditorStateSubscribeHandler(EditorUiStateBridgeService stateService)
    {
        _stateService = stateService;
    }

    public ValueTask<EditorStateSubscribeResponse?> HandleAsync(
        EditorStateSubscribeRequest request,
        BridgeRequestContext context,
        CancellationToken cancellationToken)
    {
        var response = new EditorStateSubscribeResponse
        {
            SubscriptionId = $"state-sub-{Guid.NewGuid():N}",
            Domains = request.Domains,
            DeliveryMode = request.DeliveryMode,
            Snapshot = request.FullSnapshotOnSubscribe ? _stateService.BuildSnapshot() : null,
        };

        return ValueTask.FromResult<EditorStateSubscribeResponse?>(response);
    }
}

public sealed class EditorStateRequestFullHandler : IBridgeCommandHandler<EditorStateRequestFullRequest, EditorStateRequestFullResponse>
{
    private readonly EditorUiStateBridgeService _stateService;

    public EditorStateRequestFullHandler(EditorUiStateBridgeService stateService)
    {
        _stateService = stateService;
    }

    public ValueTask<EditorStateRequestFullResponse?> HandleAsync(
        EditorStateRequestFullRequest request,
        BridgeRequestContext context,
        CancellationToken cancellationToken)
    {
        var response = new EditorStateRequestFullResponse
        {
            Snapshot = _stateService.BuildSnapshot(),
        };

        return ValueTask.FromResult<EditorStateRequestFullResponse?>(response);
    }
}

public sealed class CommandExecuteHandler : IBridgeCommandHandler<CommandExecuteRequest, CommandExecuteResponse>
{
    private readonly VoxelModelHolder _modelHolder;
    private readonly EditorUiStateBridgeService _stateService;

    public CommandExecuteHandler(VoxelModelHolder modelHolder, EditorUiStateBridgeService stateService)
    {
        _modelHolder = modelHolder;
        _stateService = stateService;
    }

    public async ValueTask<CommandExecuteResponse?> HandleAsync(
        CommandExecuteRequest request,
        BridgeRequestContext context,
        CancellationToken cancellationToken)
    {
        EnsureLoaded();
        ArgumentNullException.ThrowIfNull(request.CommandName);

        var commandName = request.CommandName.Trim();
        string message;
        string[] affectedDomains;

        if (string.Equals(commandName, "set_active_tool", StringComparison.OrdinalIgnoreCase))
        {
            var toolName = RequiredString(request.Arguments, "tool");
            if (!Enum.TryParse<EditorTool>(toolName, ignoreCase: true, out var tool))
            {
                throw new BridgeHandlerException(
                    "voxelforge.command.invalid_tool",
                    $"Unknown editor tool '{toolName}'.",
                    BridgeErrorCategories.Validation,
                    retryable: false);
            }

            _modelHolder.Session.ActiveTool = tool;
            message = $"Selected {tool.ToString().ToLowerInvariant()} tool.";
            affectedDomains = ["session"];
        }
        else if (string.Equals(commandName, "set_active_palette", StringComparison.OrdinalIgnoreCase))
        {
            var paletteIndex = RequiredByte(request.Arguments, "palette_index");
            if (paletteIndex != 0 && !_modelHolder.Model.Palette.Entries.ContainsKey(paletteIndex))
            {
                throw new BridgeHandlerException(
                    "voxelforge.command.invalid_palette_index",
                    $"Palette index {paletteIndex} does not exist in the current model.",
                    BridgeErrorCategories.Validation,
                    retryable: false);
            }

            _modelHolder.Session.ActivePaletteIndex = paletteIndex;
            message = $"Selected palette index {paletteIndex}.";
            affectedDomains = ["session"];
        }
        else
        {
            throw new BridgeHandlerException(
                "voxelforge.command.unsupported",
                $"Unsupported Electron UI command '{commandName}'.",
                BridgeErrorCategories.UnsupportedCapability,
                retryable: false);
        }

        _modelHolder.SetStatus(message);
        await _stateService.PublishFullStateAsync(message, cancellationToken).ConfigureAwait(false);

        return new CommandExecuteResponse
        {
            Success = true,
            Message = message,
            AffectedDomains = affectedDomains,
            MeshChanged = false,
            State = _stateService.BuildSnapshot(),
        };
    }

    private void EnsureLoaded()
    {
        if (!_modelHolder.IsLoaded)
        {
            throw new BridgeHandlerException(
                "voxelforge.command.not_loaded",
                "No model is currently loaded. Load a model before executing commands.",
                BridgeErrorCategories.NotFound,
                retryable: true);
        }
    }

    private static string RequiredString(IReadOnlyDictionary<string, object?> arguments, string key)
    {
        if (!arguments.TryGetValue(key, out var value) || value is null)
            ThrowMissingArgument(key);

        if (value is string s)
            return s;

        if (value is JsonElement element && element.ValueKind == JsonValueKind.String)
            return element.GetString() ?? string.Empty;

        throw new BridgeHandlerException(
            "voxelforge.command.invalid_argument",
            $"Command argument '{key}' must be a string.",
            BridgeErrorCategories.Validation,
            retryable: false);
    }

    private static byte RequiredByte(IReadOnlyDictionary<string, object?> arguments, string key)
    {
        if (!arguments.TryGetValue(key, out var value) || value is null)
            ThrowMissingArgument(key);

        int parsed;
        if (value is JsonElement element && element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var number))
            parsed = number;
        else if (value is int intValue)
            parsed = intValue;
        else if (value is long longValue && longValue >= int.MinValue && longValue <= int.MaxValue)
            parsed = (int)longValue;
        else
            throw new BridgeHandlerException(
                "voxelforge.command.invalid_argument",
                $"Command argument '{key}' must be an integer palette index.",
                BridgeErrorCategories.Validation,
                retryable: false);

        if (parsed < byte.MinValue || parsed > byte.MaxValue)
        {
            throw new BridgeHandlerException(
                "voxelforge.command.invalid_palette_index",
                $"Palette index {parsed} is outside the byte range 0-255.",
                BridgeErrorCategories.Validation,
                retryable: false);
        }

        return (byte)parsed;
    }

    private static void ThrowMissingArgument(string key)
    {
        throw new BridgeHandlerException(
            "voxelforge.command.missing_argument",
            $"Missing required command argument '{key}'.",
            BridgeErrorCategories.Validation,
            retryable: false);
    }
}

public sealed class HistoryUndoHandler : IBridgeCommandHandler<HistoryUndoRequest, HistoryCommandResponse>
{
    private readonly VoxelModelHolder _modelHolder;
    private readonly EditorUiStateBridgeService _stateService;
    private readonly MeshSubscriptionManager _meshSubscriptionManager;
    private readonly MeshChangePushService _meshPushService;

    public HistoryUndoHandler(
        VoxelModelHolder modelHolder,
        EditorUiStateBridgeService stateService,
        MeshSubscriptionManager meshSubscriptionManager,
        MeshChangePushService meshPushService)
    {
        _modelHolder = modelHolder;
        _stateService = stateService;
        _meshSubscriptionManager = meshSubscriptionManager;
        _meshPushService = meshPushService;
    }

    public async ValueTask<HistoryCommandResponse?> HandleAsync(
        HistoryUndoRequest request,
        BridgeRequestContext context,
        CancellationToken cancellationToken)
    {
        if (!_modelHolder.UndoStack.CanUndo)
        {
            var idleMessage = "Nothing to undo.";
            _modelHolder.SetStatus(idleMessage);
            return new HistoryCommandResponse
            {
                Success = true,
                Message = idleMessage,
                MeshChanged = false,
                State = _stateService.BuildSnapshot(),
            };
        }

        _modelHolder.UndoStack.Undo();
        _modelHolder.MarkDirty(true);
        var message = "Undo applied.";
        _modelHolder.SetStatus(message);
        _meshSubscriptionManager.RecordFullDirty(_modelHolder.ModelId, _modelHolder.Model);
        await _meshPushService.PushMeshUpdateAsync(cancellationToken).ConfigureAwait(false);
        await _stateService.PublishFullStateAsync(message, cancellationToken).ConfigureAwait(false);

        return new HistoryCommandResponse
        {
            Success = true,
            Message = message,
            MeshChanged = true,
            State = _stateService.BuildSnapshot(),
        };
    }
}

public sealed class HistoryRedoHandler : IBridgeCommandHandler<HistoryRedoRequest, HistoryCommandResponse>
{
    private readonly VoxelModelHolder _modelHolder;
    private readonly EditorUiStateBridgeService _stateService;
    private readonly MeshSubscriptionManager _meshSubscriptionManager;
    private readonly MeshChangePushService _meshPushService;

    public HistoryRedoHandler(
        VoxelModelHolder modelHolder,
        EditorUiStateBridgeService stateService,
        MeshSubscriptionManager meshSubscriptionManager,
        MeshChangePushService meshPushService)
    {
        _modelHolder = modelHolder;
        _stateService = stateService;
        _meshSubscriptionManager = meshSubscriptionManager;
        _meshPushService = meshPushService;
    }

    public async ValueTask<HistoryCommandResponse?> HandleAsync(
        HistoryRedoRequest request,
        BridgeRequestContext context,
        CancellationToken cancellationToken)
    {
        if (!_modelHolder.UndoStack.CanRedo)
        {
            var idleMessage = "Nothing to redo.";
            _modelHolder.SetStatus(idleMessage);
            return new HistoryCommandResponse
            {
                Success = true,
                Message = idleMessage,
                MeshChanged = false,
                State = _stateService.BuildSnapshot(),
            };
        }

        _modelHolder.UndoStack.Redo();
        _modelHolder.MarkDirty(true);
        var message = "Redo applied.";
        _modelHolder.SetStatus(message);
        _meshSubscriptionManager.RecordFullDirty(_modelHolder.ModelId, _modelHolder.Model);
        await _meshPushService.PushMeshUpdateAsync(cancellationToken).ConfigureAwait(false);
        await _stateService.PublishFullStateAsync(message, cancellationToken).ConfigureAwait(false);

        return new HistoryCommandResponse
        {
            Success = true,
            Message = message,
            MeshChanged = true,
            State = _stateService.BuildSnapshot(),
        };
    }
}

public sealed class ProjectSaveHandler : IBridgeCommandHandler<ProjectSaveRequest, ProjectCommandResponse>
{
    private readonly VoxelModelHolder _modelHolder;
    private readonly ProjectLifecycleService _projectService;
    private readonly EditorUiStateBridgeService _stateService;
    private readonly VoxelForge.App.Events.IEventPublisher _events;

    public ProjectSaveHandler(
        VoxelModelHolder modelHolder,
        ProjectLifecycleService projectService,
        EditorUiStateBridgeService stateService,
        VoxelForge.App.Events.IEventPublisher events)
    {
        _modelHolder = modelHolder;
        _projectService = projectService;
        _stateService = stateService;
        _events = events;
    }

    public async ValueTask<ProjectCommandResponse?> HandleAsync(
        ProjectSaveRequest request,
        BridgeRequestContext context,
        CancellationToken cancellationToken)
    {
        var path = RequiredPath(request.Path, "save");
        var result = _projectService.Save(_modelHolder.Document, _events, new SaveProjectRequest(path));
        if (result.Success)
        {
            _modelHolder.SetProjectPath(path);
            _modelHolder.MarkDirty(false);
        }

        _modelHolder.SetStatus(result.Message);
        await _stateService.PublishFullStateAsync(result.Message, cancellationToken).ConfigureAwait(false);

        return new ProjectCommandResponse
        {
            Success = result.Success,
            Message = result.Message,
            MeshChanged = false,
            State = _stateService.BuildSnapshot(),
        };
    }

    private static string RequiredPath(string path, string operation)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new BridgeHandlerException(
                $"voxelforge.project.{operation}_missing_path",
                $"Project {operation} requires a non-empty path or project name.",
                BridgeErrorCategories.Validation,
                retryable: false);
        }

        return path.Trim();
    }
}

public sealed class ProjectLoadHandler : IBridgeCommandHandler<ProjectLoadRequest, ProjectCommandResponse>
{
    private readonly VoxelModelHolder _modelHolder;
    private readonly ProjectLifecycleService _projectService;
    private readonly EditorUiStateBridgeService _stateService;
    private readonly MeshSubscriptionManager _meshSubscriptionManager;
    private readonly MeshChangePushService _meshPushService;
    private readonly VoxelForge.App.Events.IEventPublisher _events;

    public ProjectLoadHandler(
        VoxelModelHolder modelHolder,
        ProjectLifecycleService projectService,
        EditorUiStateBridgeService stateService,
        MeshSubscriptionManager meshSubscriptionManager,
        MeshChangePushService meshPushService,
        VoxelForge.App.Events.IEventPublisher events)
    {
        _modelHolder = modelHolder;
        _projectService = projectService;
        _stateService = stateService;
        _meshSubscriptionManager = meshSubscriptionManager;
        _meshPushService = meshPushService;
        _events = events;
    }

    public async ValueTask<ProjectCommandResponse?> HandleAsync(
        ProjectLoadRequest request,
        BridgeRequestContext context,
        CancellationToken cancellationToken)
    {
        var path = RequiredPath(request.Path, "load");
        var result = _projectService.Load(_modelHolder.Document, _modelHolder.UndoStack, _events, new LoadProjectRequest(path));
        if (result.Success)
        {
            _modelHolder.SetProjectPath(path);
            _modelHolder.SetModelId(Path.GetFileNameWithoutExtension(path));
            _modelHolder.MarkDirty(false);
            _modelHolder.SetStatus(result.Message);
            _meshSubscriptionManager.RecordFullDirty(_modelHolder.ModelId, _modelHolder.Model);
            await _meshPushService.PushMeshUpdateAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _modelHolder.SetStatus(result.Message);
        }

        await _stateService.PublishFullStateAsync(result.Message, cancellationToken).ConfigureAwait(false);

        return new ProjectCommandResponse
        {
            Success = result.Success,
            Message = result.Message,
            MeshChanged = result.Success,
            State = _stateService.BuildSnapshot(),
        };
    }

    private static string RequiredPath(string path, string operation)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new BridgeHandlerException(
                $"voxelforge.project.{operation}_missing_path",
                $"Project {operation} requires a non-empty path or project name.",
                BridgeErrorCategories.Validation,
                retryable: false);
        }

        return path.Trim();
    }
}
