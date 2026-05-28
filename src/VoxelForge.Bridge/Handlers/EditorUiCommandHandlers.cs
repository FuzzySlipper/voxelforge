using System.Diagnostics;
using System.Text.Json;
using Den.Bridge.Abstractions;
using Den.Bridge.Protocol;
using Microsoft.Extensions.Logging;
using VoxelForge.App;
using VoxelForge.App.Events;
using VoxelForge.App.Services;
using VoxelForge.Bridge.Protocol;
using VoxelForge.Core;

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
    private readonly VoxelEditingService _voxelEditing;
    private readonly IEventPublisher _events;
    private readonly MeshSubscriptionManager _meshSubscriptionManager;
    private readonly MeshChangePushService _meshPushService;
    private readonly ILogger<CommandExecuteHandler> _logger;
    private readonly IBridgeEventPublisher _bridgeEventPublisher;

    private static readonly TimeSpan LatencyWarningThreshold = TimeSpan.FromMilliseconds(100);

    public CommandExecuteHandler(
        VoxelModelHolder modelHolder,
        EditorUiStateBridgeService stateService,
        VoxelEditingService voxelEditing,
        IEventPublisher events,
        MeshSubscriptionManager meshSubscriptionManager,
        MeshChangePushService meshPushService,
        ILogger<CommandExecuteHandler> logger,
        IBridgeEventPublisher bridgeEventPublisher)
    {
        _modelHolder = modelHolder;
        _stateService = stateService;
        _voxelEditing = voxelEditing;
        _events = events;
        _meshSubscriptionManager = meshSubscriptionManager;
        _meshPushService = meshPushService;
        _logger = logger;
        _bridgeEventPublisher = bridgeEventPublisher;
    }

    public async ValueTask<CommandExecuteResponse?> HandleAsync(
        CommandExecuteRequest request,
        BridgeRequestContext context,
        CancellationToken cancellationToken)
    {
        EnsureLoaded();
        ArgumentNullException.ThrowIfNull(request.CommandName);

        var commandName = request.CommandName.Trim();
        var stopwatch = Stopwatch.StartNew();
        long meshUpdateMs = 0;

        string message;
        string[] affectedDomains;
        bool meshChanged;

        try
        {
            // ── Session commands (no mesh change) ──
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
                meshChanged = false;
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
                meshChanged = false;
            }
            // ── Viewport editing commands (mesh change) ──
            else if (string.Equals(commandName, "place_voxel", StringComparison.OrdinalIgnoreCase))
            {
                var pos = RequiredPoint3(request.Arguments);
                var paletteIdx = RequiredByte(request.Arguments, "palette_index");
                var result = _voxelEditing.SetVoxel(
                    _modelHolder.Document, _modelHolder.UndoStack, _events,
                    new SetVoxelRequest(pos, paletteIdx));
                message = result.Message;
                affectedDomains = ["document", "session", "history"];
                meshChanged = result.Success;
                if (meshChanged) meshUpdateMs = await TimedMeshPushAsync(cancellationToken).ConfigureAwait(false);
            }
            else if (string.Equals(commandName, "remove_voxel", StringComparison.OrdinalIgnoreCase))
            {
                var pos = RequiredPoint3(request.Arguments);
                var result = _voxelEditing.RemoveVoxel(
                    _modelHolder.Document, _modelHolder.UndoStack, _events,
                    new RemoveVoxelRequest(pos));
                message = result.Message;
                affectedDomains = ["document", "session", "history"];
                meshChanged = result.Success;
                if (meshChanged) meshUpdateMs = await TimedMeshPushAsync(cancellationToken).ConfigureAwait(false);
            }
            else if (string.Equals(commandName, "paint_voxel", StringComparison.OrdinalIgnoreCase))
            {
                var pos = RequiredPoint3(request.Arguments);
                var paletteIdx = RequiredByte(request.Arguments, "palette_index");
                var result = _voxelEditing.PaintVoxel(
                    _modelHolder.Document, _modelHolder.UndoStack, _events,
                    new PaintVoxelRequest(pos, paletteIdx));
                message = result.Message;
                affectedDomains = ["document", "session", "history"];
                meshChanged = result.Success;
                if (meshChanged) meshUpdateMs = await TimedMeshPushAsync(cancellationToken).ConfigureAwait(false);
            }
            else if (string.Equals(commandName, "fill_region", StringComparison.OrdinalIgnoreCase))
            {
                var min = RequiredPoint3(request.Arguments, "min_x", "min_y", "min_z");
                var max = RequiredPoint3(request.Arguments, "max_x", "max_y", "max_z");
                var paletteIdx = RequiredByte(request.Arguments, "palette_index");
                var result = _voxelEditing.FillRegion(
                    _modelHolder.Document, _modelHolder.UndoStack, _events,
                    new FillVoxelRegionRequest(min, max, paletteIdx));
                message = result.Message;
                affectedDomains = ["document", "session", "history"];
                meshChanged = result.Success;
                if (meshChanged) meshUpdateMs = await TimedMeshPushAsync(cancellationToken).ConfigureAwait(false);
            }
            else if (string.Equals(commandName, "clear_model", StringComparison.OrdinalIgnoreCase))
            {
                var result = _voxelEditing.Clear(
                    _modelHolder.Document, _modelHolder.UndoStack, _events);
                message = result.Message;
                affectedDomains = ["document", "session", "history"];
                meshChanged = result.Success;
                if (meshChanged) meshUpdateMs = await TimedMeshPushAsync(cancellationToken).ConfigureAwait(false);
            }
            // ── Selection commands (no mesh change) ──
            else if (string.Equals(commandName, "select_voxel", StringComparison.OrdinalIgnoreCase))
            {
                var pos = RequiredPoint3(request.Arguments);
                _modelHolder.Session.SelectedVoxels.Clear();
                _modelHolder.Session.SelectedVoxels.Add(pos);
                message = $"Selected ({pos.X},{pos.Y},{pos.Z}).";
                affectedDomains = ["session"];
                meshChanged = false;
            }
            else if (string.Equals(commandName, "add_to_selection", StringComparison.OrdinalIgnoreCase))
            {
                var pos = RequiredPoint3(request.Arguments);
                _modelHolder.Session.SelectedVoxels.Add(pos);
                message = $"Added ({pos.X},{pos.Y},{pos.Z}) to selection.";
                affectedDomains = ["session"];
                meshChanged = false;
            }
            else if (string.Equals(commandName, "clear_selection", StringComparison.OrdinalIgnoreCase))
            {
                _modelHolder.Session.SelectedVoxels.Clear();
                message = "Selection cleared.";
                affectedDomains = ["session"];
                meshChanged = false;
            }
            else
            {
                throw new BridgeHandlerException(
                    "voxelforge.command.unsupported",
                    $"Unsupported Electron UI command '{commandName}'.",
                    BridgeErrorCategories.UnsupportedCapability,
                    retryable: false);
            }
        }
        finally
        {
            stopwatch.Stop();
        }

        _modelHolder.SetStatus(message);
        await _stateService.PublishFullStateAsync(message, cancellationToken).ConfigureAwait(false);

        var totalMs = stopwatch.ElapsedMilliseconds;
        if (totalMs > LatencyWarningThreshold.TotalMilliseconds)
        {
            _logger.LogWarning(
                "Editing command '{CommandName}' took {TotalMs}ms (exceeds {ThresholdMs}ms threshold)",
                commandName, totalMs, LatencyWarningThreshold.TotalMilliseconds);
        }

        // Publish latency diagnostic event unconditionally
        var latencyPayload = new EditingLatencyEventPayload
        {
            CommandName = commandName,
            CSharpProcessingMs = stopwatch.ElapsedMilliseconds,
            MeshUpdateMs = meshUpdateMs,
            TotalMs = totalMs,
            Timestamp = DateTimeOffset.UtcNow,
        };
        var latencyFrame = new BridgeEventFrame
        {
            EventId = $"evt-latency-{Guid.NewGuid():N}",
            Sequence = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Event = "voxelforge.diagnostics.editing_latency",
            Payload = BridgeJson.ToElement(latencyPayload),
        };
        await _bridgeEventPublisher.PublishAsync(latencyFrame, cancellationToken).ConfigureAwait(false);

        return new CommandExecuteResponse
        {
            Success = true,
            Message = message,
            AffectedDomains = affectedDomains,
            MeshChanged = meshChanged,
            State = _stateService.BuildSnapshot(),
        };
    }

    private async ValueTask<long> TimedMeshPushAsync(CancellationToken cancellationToken)
    {
        var meshSw = Stopwatch.StartNew();
        await PushMeshUpdateAsync(cancellationToken).ConfigureAwait(false);
        meshSw.Stop();
        return meshSw.ElapsedMilliseconds;
    }

    private async ValueTask PushMeshUpdateAsync(CancellationToken cancellationToken)
    {
        _modelHolder.MarkDirty(true);
        _meshSubscriptionManager.RecordFullDirty(_modelHolder.ModelId, _modelHolder.Model);
        await _meshPushService.PushMeshUpdateAsync(cancellationToken).ConfigureAwait(false);
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

    private static Point3 RequiredPoint3(IReadOnlyDictionary<string, object?> arguments, string xKey = "x", string yKey = "y", string zKey = "z")
    {
        var x = RequiredInt(arguments, xKey);
        var y = RequiredInt(arguments, yKey);
        var z = RequiredInt(arguments, zKey);
        return new Point3(x, y, z);
    }

    private static int RequiredInt(IReadOnlyDictionary<string, object?> arguments, string key)
    {
        if (!arguments.TryGetValue(key, out var value) || value is null)
            ThrowMissingArgument(key);

        if (value is JsonElement element && element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var number))
            return number;
        if (value is int intValue)
            return intValue;
        if (value is long longValue && longValue >= int.MinValue && longValue <= int.MaxValue)
            return (int)longValue;

        throw new BridgeHandlerException(
            "voxelforge.command.invalid_argument",
            $"Command argument '{key}' must be an integer.",
            BridgeErrorCategories.Validation,
            retryable: false);
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
        var parsed = RequiredInt(arguments, key);
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

public sealed class ProjectNewHandler : IBridgeCommandHandler<ProjectNewRequest, ProjectCommandResponse>
{
    private readonly VoxelModelHolder _modelHolder;
    private readonly EditorUiStateBridgeService _stateService;
    private readonly MeshSubscriptionManager _meshSubscriptionManager;
    private readonly MeshChangePushService _meshPushService;

    public ProjectNewHandler(
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

    public async ValueTask<ProjectCommandResponse?> HandleAsync(
        ProjectNewRequest request,
        BridgeRequestContext context,
        CancellationToken cancellationToken)
    {
        _modelHolder.ResetToNewProject(request.Name);
        _modelHolder.MarkDirty(false);

        var message = $"Created new project '{_modelHolder.Workspace.CurrentModelName}'.";
        _modelHolder.SetStatus(message);

        // Push fresh mesh and state
        _meshSubscriptionManager.RecordFullDirty(_modelHolder.ModelId, _modelHolder.Model);
        await _meshPushService.PushMeshUpdateAsync(cancellationToken).ConfigureAwait(false);
        await _stateService.PublishFullStateAsync(message, cancellationToken).ConfigureAwait(false);

        return new ProjectCommandResponse
        {
            Success = true,
            Message = message,
            MeshChanged = true,
            State = _stateService.BuildSnapshot(),
        };
    }
}
