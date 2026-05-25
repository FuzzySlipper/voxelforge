using VoxelForge.App.Commands;
using VoxelForge.App.Events;
using VoxelForge.App.Reference;

namespace VoxelForge.App.Workspaces;

/// <summary>
/// Shared App-layer mutable truth aggregate for the state currently split
/// between <see cref="VoxelForge.Mcp.VoxelForgeMcpSession"/> and
/// <see cref="VoxelForge.Bridge.Handlers.VoxelModelHolder"/>.
/// <para>
/// Both MCP and Bridge host their own singleton instance of this state.
/// The state shape and operations are common; host-only details (SSE channels,
/// HTTP/request details, Chromium capture config, Bridge WebSocket/subscription
/// objects, Electron IPC/window state) do NOT belong here.
/// </para>
/// <para>
/// ESS rules: mutable truth lives in *State. Services operate over explicit state
/// arguments and do not hide durable state in service instance fields.
/// </para>
/// </summary>
public sealed class VoxelForgeWorkspaceState
{
    private long _revision;

    /// <summary>
    /// Create a new workspace state with default document/undo/ref state.
    /// </summary>
    public VoxelForgeWorkspaceState(
        EditorDocumentState document,
        EditorSessionState session,
        UndoHistoryState undoHistory,
        UndoStack undoStack,
        IEventDispatcher events,
        ReferenceModelState referenceModels,
        ReferenceImageState referenceImages)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(undoHistory);
        ArgumentNullException.ThrowIfNull(undoStack);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(referenceModels);
        ArgumentNullException.ThrowIfNull(referenceImages);

        Document = document;
        Session = session;
        UndoHistory = undoHistory;
        UndoStack = undoStack;
        Events = events;
        ReferenceModels = referenceModels;
        ReferenceImages = referenceImages;
    }

    /// <summary>
    /// Document state: active model, labels, clips.
    /// </summary>
    public EditorDocumentState Document { get; set; }

    /// <summary>
    /// Session state: active tool, palette index, selection, frame.
    /// </summary>
    public EditorSessionState Session { get; }

    /// <summary>
    /// Undo history state: undo/redo command lists.
    /// </summary>
    public UndoHistoryState UndoHistory { get; }

    /// <summary>
    /// Undo stack: command execution and undo/redo orchestration.
    /// </summary>
    public UndoStack UndoStack { get; }

    /// <summary>
    /// Event dispatcher for application events.
    /// </summary>
    public IEventDispatcher Events { get; }

    /// <summary>
    /// Reference model state: loaded reference 3D models.
    /// </summary>
    public ReferenceModelState ReferenceModels { get; }

    /// <summary>
    /// Reference image state: loaded reference images.
    /// </summary>
    public ReferenceImageState ReferenceImages { get; }

    /// <summary>
    /// Stable model identifier.
    /// </summary>
    public string ModelId { get; set; } = "default";

    /// <summary>
    /// Current project file path, or null if unsaved.
    /// </summary>
    public string? ProjectPath { get; set; }

    /// <summary>
    /// Human-readable model name for display.
    /// </summary>
    public string CurrentModelName { get; set; } = "untitled";

    /// <summary>
    /// Whether the current document has unsaved changes.
    /// </summary>
    public bool IsDirty { get; set; }

    /// <summary>
    /// Current status message for UI display.
    /// </summary>
    public string StatusMessage { get; set; } = "Ready";

    /// <summary>
    /// Monotonically increasing revision counter incremented on model/palette/state
    /// changes. Used by transports to detect when to re-fetch render data.
    /// </summary>
    public long Revision
    {
        get => Interlocked.Read(ref _revision);
        set => Interlocked.Exchange(ref _revision, value);
    }

    /// <summary>
    /// Atomically increment the revision counter and return the new value.
    /// </summary>
    public long IncrementRevision() => Interlocked.Increment(ref _revision);

    /// <summary>
    /// Whether a model/document has been loaded.
    /// </summary>
    public bool IsLoaded { get; set; }
}
