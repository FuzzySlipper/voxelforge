using Microsoft.Extensions.Logging;
using VoxelForge.App;
using VoxelForge.App.Commands;
using VoxelForge.App.Console;
using VoxelForge.App.Events;
using VoxelForge.Core;

namespace VoxelForge.Mcp;

/// <summary>
/// In-memory session state for the headless MCP server.
/// </summary>
public sealed class VoxelForgeMcpSession
{
    private readonly object _syncRoot = new();
    private int _viewerRevision;

    public VoxelForgeMcpSession(EditorConfigState config, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var model = new VoxelModel(loggerFactory.CreateLogger<VoxelModel>())
        {
            GridHint = config.DefaultGridHint,
        };
        var labels = new LabelIndex(loggerFactory.CreateLogger<LabelIndex>());
        Document = new EditorDocumentState(model, labels);
        Events = new ApplicationEventDispatcher();
        var undoHistory = new UndoHistoryState(config.MaxUndoDepth);
        var undoHistoryService = new UndoHistoryService(loggerFactory.CreateLogger<UndoHistoryService>());
        UndoStack = new UndoStack(undoHistory, undoHistoryService, Events);
        CommandContext = new CommandContext
        {
            Document = Document,
            UndoStack = UndoStack,
            Events = Events,
            Mode = ExecutionMode.Headless,
        };

        // Subscribe to events that indicate viewer-relevant model changes.
        Events.Register<VoxelModelChangedEvent>(new ViewerRevisionEventHandler(() => IncrementViewerRevision()));
        Events.Register<PaletteChangedEvent>(new ViewerRevisionEventHandler(() => IncrementViewerRevision()));
        Events.Register<UndoHistoryChangedEvent>(new ViewerRevisionEventHandler(() => IncrementViewerRevision()));
        Events.Register<ProjectLoadedEvent>(new ViewerRevisionEventHandler(() => IncrementViewerRevision()));
    }

    public EditorDocumentState Document { get; }

    public UndoStack UndoStack { get; }

    public IEventDispatcher Events { get; }

    public CommandContext CommandContext { get; }

    public string CurrentModelName { get; set; } = "untitled";

    public object SyncRoot => _syncRoot;

    /// <summary>
    /// Monotonically increasing revision counter incremented on model/palette/state
    /// changes. Used by the browser viewer to detect when to re-fetch mesh data.
    /// </summary>
    public int ViewerRevision
    {
        get
        {
            lock (_syncRoot) { return _viewerRevision; }
        }
    }

    /// <summary>
    /// Increment the viewer revision. Thread-safe.
    /// </summary>
    public void IncrementViewerRevision()
    {
        lock (_syncRoot) { _viewerRevision++; }
    }

    /// <summary>
    /// Internal event handler that delegates to an action, used for revision tracking.
    /// </summary>
    private sealed class ViewerRevisionEventHandler :
        IEventHandler<VoxelModelChangedEvent>,
        IEventHandler<PaletteChangedEvent>,
        IEventHandler<UndoHistoryChangedEvent>,
        IEventHandler<ProjectLoadedEvent>
    {
        private readonly Action _onEvent;

        public ViewerRevisionEventHandler(Action onEvent)
        {
            _onEvent = onEvent;
        }

        public void Handle(VoxelModelChangedEvent e) => _onEvent();
        public void Handle(PaletteChangedEvent e) => _onEvent();
        public void Handle(UndoHistoryChangedEvent e) => _onEvent();
        public void Handle(ProjectLoadedEvent e) => _onEvent();
    }
}
