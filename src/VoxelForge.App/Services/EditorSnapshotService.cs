using VoxelForge.App.Commands;
using VoxelForge.App.Events;
using VoxelForge.App.Snapshots;
using VoxelForge.Core;
using VoxelForge.Core.Meshing;

namespace VoxelForge.App.Services;

/// <summary>
/// Stateless service that produces renderer-neutral snapshots of editor state.
/// Reads from explicit state objects (<see cref="EditorDocumentState"/>,
/// <see cref="EditorSessionState"/>, etc.) and produces immutable snapshots
/// suitable for serialization to any renderer (WebGL, Electron, headless).
/// <para>
/// This service does NOT mutate any state. Mutations flow through
/// <see cref="Commands.IEditorCommand"/> and <see cref="Commands.UndoStack"/>.
/// </para>
/// </summary>
public sealed class EditorSnapshotService
{
    private readonly MeshSnapshotService _meshSnapshotService;
    private readonly PaletteSnapshotService _paletteSnapshotService;

    public EditorSnapshotService(
        MeshSnapshotService meshSnapshotService,
        PaletteSnapshotService paletteSnapshotService)
    {
        ArgumentNullException.ThrowIfNull(meshSnapshotService);
        ArgumentNullException.ThrowIfNull(paletteSnapshotService);
        _meshSnapshotService = meshSnapshotService;
        _paletteSnapshotService = paletteSnapshotService;
    }

    /// <summary>
    /// Build a full editor snapshot from the current document, session, and undo state.
    /// Captures mesh geometry, palette, labels, session, undo history, and diagnostics.
    /// </summary>
    public EditorSnapshot BuildSnapshot(
        EditorDocumentState document,
        EditorSessionState session,
        UndoHistoryState undoHistory,
        bool isDirty = false,
        string modelId = "untitled",
        IReadOnlyList<StatusEntrySnapshot>? recentStatuses = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(undoHistory);

        var mesh = _meshSnapshotService.BuildSnapshot(document.Model);
        var palette = _paletteSnapshotService.BuildSnapshot(document.Model.Palette);

        return new EditorSnapshot
        {
            Document = BuildDocumentSnapshot(document, isDirty, modelId),
            Session = BuildSessionSnapshot(document, session),
            Palette = palette,
            Mesh = mesh,
            Labels = BuildLabelsSnapshot(document.Labels),
            UndoHistory = BuildUndoHistorySnapshot(undoHistory),
            Diagnostics = BuildDiagnosticsSnapshot(recentStatuses),
        };
    }

    /// <summary>
    /// Build a document-level snapshot from the current document state.
    /// </summary>
    public static DocumentSnapshot BuildDocumentSnapshot(
        EditorDocumentState document,
        bool isDirty = false,
        string modelId = "untitled")
    {
        ArgumentNullException.ThrowIfNull(document);

        var bounds = document.Model.GetBounds();
        BoundsSnapshot? boundsSnapshot = null;
        if (bounds is { } b)
        {
            boundsSnapshot = new BoundsSnapshot
            {
                MinX = b.Min.X,
                MinY = b.Min.Y,
                MinZ = b.Min.Z,
                MaxX = b.Max.X,
                MaxY = b.Max.Y,
                MaxZ = b.Max.Z,
            };
        }

        return new DocumentSnapshot
        {
            ModelId = modelId,
            VoxelCount = document.Model.GetVoxelCount(),
            Bounds = boundsSnapshot,
            GridHint = document.Model.GridHint,
            ClipCount = document.Clips.Count,
            IsDirty = isDirty,
        };
    }

    /// <summary>
    /// Build a session-level snapshot from document and session state.
    /// </summary>
    public static SessionSnapshot BuildSessionSnapshot(
        EditorDocumentState document,
        EditorSessionState session)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(session);

        var selectedVoxels = session.SelectedVoxels
            .Select(p => new Point3Snapshot(p.X, p.Y, p.Z))
            .ToList();

        return new SessionSnapshot
        {
            ActiveTool = session.ActiveTool.ToString(),
            ActivePaletteIndex = session.ActivePaletteIndex,
            SelectedVoxels = selectedVoxels,
            ActiveRegionId = session.ActiveRegion?.Value,
            ActiveFrameIndex = session.ActiveFrameIndex,
        };
    }

    /// <summary>
    /// Build a selection snapshot from session state.
    /// </summary>
    public static SelectionSnapshot BuildSelectionSnapshot(EditorSessionState session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var voxels = session.SelectedVoxels
            .Select(p => new Point3Snapshot(p.X, p.Y, p.Z))
            .ToList();

        BoundsSnapshot? bounds = null;
        if (session.SelectedVoxels.Count > 0)
        {
            int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;
            foreach (var p in session.SelectedVoxels)
            {
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Z < minZ) minZ = p.Z;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
                if (p.Z > maxZ) maxZ = p.Z;
            }
            bounds = new BoundsSnapshot
            {
                MinX = minX, MinY = minY, MinZ = minZ,
                MaxX = maxX, MaxY = maxY, MaxZ = maxZ,
            };
        }

        return new SelectionSnapshot
        {
            Voxels = voxels,
            ActiveRegionId = session.ActiveRegion?.Value,
            Bounds = bounds,
        };
    }

    /// <summary>
    /// Build a labels snapshot from the current label index.
    /// </summary>
    public static LabelsSnapshot BuildLabelsSnapshot(LabelIndex labels)
    {
        ArgumentNullException.ThrowIfNull(labels);

        var regions = labels.Regions
            .OrderBy(kvp => kvp.Key.Value, StringComparer.Ordinal)
            .Select(kvp => new RegionSnapshot
            {
                RegionId = kvp.Key.Value,
                Name = kvp.Value.Name,
                VoxelCount = kvp.Value.Voxels.Count,
                ParentId = kvp.Value.ParentId?.Value,
            })
            .ToList();

        int labeledVoxelCount = 0;
        foreach (var kvp in labels.Regions)
            labeledVoxelCount += kvp.Value.Voxels.Count;

        return new LabelsSnapshot
        {
            Regions = regions,
            LabeledVoxelCount = labeledVoxelCount,
        };
    }

    /// <summary>
    /// Build an undo history snapshot.
    /// </summary>
    public static UndoHistorySnapshot BuildUndoHistorySnapshot(UndoHistoryState undoHistory)
    {
        ArgumentNullException.ThrowIfNull(undoHistory);

        string? lastCmd = null;
        if (undoHistory.UndoCommands.Count > 0)
        {
            // LinkedList<T> doesn't have an indexer; get the last element
            var lastNode = undoHistory.UndoCommands.Last;
            if (lastNode is not null)
                lastCmd = lastNode.ValueRef.Description;
        }

        return new UndoHistorySnapshot
        {
            CanUndo = undoHistory.UndoCount > 0,
            CanRedo = undoHistory.RedoCount > 0,
            UndoDepth = undoHistory.UndoCount,
            RedoDepth = undoHistory.RedoCount,
            LastCommandDescription = lastCmd,
        };
    }

    /// <summary>
    /// Build a diagnostics snapshot from captured status events.
    /// </summary>
    public static DiagnosticsSnapshot BuildDiagnosticsSnapshot(
        IReadOnlyList<StatusEntrySnapshot>? recentStatuses = null)
    {
        return new DiagnosticsSnapshot
        {
            Timestamp = DateTimeOffset.UtcNow,
            RecentStatuses = recentStatuses ?? [],
        };
    }

    /// <summary>
    /// Convert an <see cref="EditorStatusEvent"/> to a <see cref="StatusEntrySnapshot"/>.
    /// </summary>
    public static StatusEntrySnapshot ToStatusEntry(EditorStatusEvent statusEvent)
    {
        ArgumentNullException.ThrowIfNull(statusEvent);

        return new StatusEntrySnapshot
        {
            Severity = statusEvent.Severity.ToString().ToLowerInvariant(),
            Source = statusEvent.Source,
            Message = statusEvent.Message,
        };
    }
}