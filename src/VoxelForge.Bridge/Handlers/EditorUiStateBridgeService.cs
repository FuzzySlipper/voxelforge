using Den.Bridge.Abstractions;
using Den.Bridge.Protocol;
using VoxelForge.App;
using VoxelForge.App.Services;
using VoxelForge.Bridge.Protocol;

namespace VoxelForge.Bridge.Handlers;

/// <summary>
/// Builds and publishes authoritative editor UI state snapshots for Electron.
/// The snapshots are renderer-neutral and deliberately exclude mesh buffers.
/// </summary>
public sealed class EditorUiStateBridgeService
{
    private readonly VoxelModelHolder _modelHolder;
    private readonly PaletteSnapshotService _paletteService;
    private readonly IBridgeEventPublisher _eventPublisher;
    private long _stateSequence;

    public EditorUiStateBridgeService(
        VoxelModelHolder modelHolder,
        PaletteSnapshotService paletteService,
        IBridgeEventPublisher eventPublisher)
    {
        _modelHolder = modelHolder;
        _paletteService = paletteService;
        _eventPublisher = eventPublisher;
    }

    public EditorUiStateSnapshot BuildSnapshot()
    {
        EnsureLoaded();

        var document = _modelHolder.Document;
        var bounds = document.Model.GetBounds();
        BoundsDto? boundsDto = null;
        if (bounds is { } b)
        {
            boundsDto = new BoundsDto
            {
                MinX = b.Min.X,
                MinY = b.Min.Y,
                MinZ = b.Min.Z,
                MaxX = b.Max.X,
                MaxY = b.Max.Y,
                MaxZ = b.Max.Z,
            };
        }

        var palette = _paletteService.BuildSnapshot(document.Model.Palette);
        var entries = new PaletteEntryResponse[palette.Entries.Count];
        for (int i = 0; i < palette.Entries.Count; i++)
        {
            var entry = palette.Entries[i];
            entries[i] = new PaletteEntryResponse
            {
                Index = entry.Index,
                Name = entry.Name,
                Color = $"#{entry.R:X2}{entry.G:X2}{entry.B:X2}",
                A = entry.A,
                Visible = entry.Index != 0,
            };
        }

        var historySnapshot = EditorSnapshotService.BuildUndoHistorySnapshot(_modelHolder.UndoHistory);

        return new EditorUiStateSnapshot
        {
            ModelId = _modelHolder.ModelId,
            ProjectPath = _modelHolder.ProjectPath,
            IsDirty = _modelHolder.IsDirty,
            VoxelCount = document.Model.GetVoxelCount(),
            Bounds = boundsDto,
            GridHint = document.Model.GridHint,
            ActiveTool = _modelHolder.Session.ActiveTool.ToString().ToLowerInvariant(),
            ActivePaletteIndex = _modelHolder.Session.ActivePaletteIndex,
            AvailableTools = GetAvailableTools(),
            PaletteEntries = entries,
            PaletteEntryCount = entries.Length,
            CanUndo = historySnapshot.CanUndo,
            CanRedo = historySnapshot.CanRedo,
            UndoDepth = historySnapshot.UndoDepth,
            RedoDepth = historySnapshot.RedoDepth,
            LastCommandDescription = historySnapshot.LastCommandDescription,
            SelectedVoxelCount = _modelHolder.Session.SelectedVoxels.Count,
            ActiveFrameIndex = _modelHolder.Session.ActiveFrameIndex,
            StatusMessage = _modelHolder.StatusMessage,
            Timestamp = DateTimeOffset.UtcNow,
        };
    }

    public async ValueTask PublishFullStateAsync(string statusMessage, CancellationToken cancellationToken)
    {
        if (!_modelHolder.IsLoaded)
            return;

        _modelHolder.SetStatus(statusMessage);
        var sequence = Interlocked.Increment(ref _stateSequence);
        var payload = new EditorStateDeltaEventPayload
        {
            Domain = "editor",
            Sequence = sequence,
            Timestamp = DateTimeOffset.UtcNow,
            Full = true,
            Snapshot = BuildSnapshot(),
        };

        var frame = new BridgeEventFrame
        {
            EventId = $"evt-state-{Guid.NewGuid():N}",
            Sequence = sequence,
            Event = "voxelforge.state.delta",
            Payload = BridgeJson.ToElement(payload),
        };

        await _eventPublisher.PublishAsync(frame, cancellationToken).ConfigureAwait(false);
    }

    private static string[] GetAvailableTools()
    {
        var values = Enum.GetValues<EditorTool>();
        var names = new string[values.Length];
        for (int i = 0; i < values.Length; i++)
            names[i] = values[i].ToString().ToLowerInvariant();
        return names;
    }

    private void EnsureLoaded()
    {
        if (!_modelHolder.IsLoaded)
        {
            throw new BridgeHandlerException(
                "voxelforge.state.not_loaded",
                "No model is currently loaded. Load a model before requesting editor state.",
                BridgeErrorCategories.NotFound,
                retryable: true);
        }
    }
}
