using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using VoxelForge.App;
using VoxelForge.App.Commands;
using VoxelForge.App.Console;
using VoxelForge.App.Events;
using VoxelForge.App.Reference;
using VoxelForge.App.Workspaces;
using VoxelForge.Core;

namespace VoxelForge.Mcp;

/// <summary>
/// In-memory session state for the headless MCP server.
/// Hosts a <see cref="VoxelForgeWorkspaceState"/> as the authoritative shared state.
/// Existing public properties delegate to <c>Workspace</c> for compatibility.
/// </summary>
public sealed class VoxelForgeMcpSession
{
    private readonly object _syncRoot = new();
    private readonly List<Channel<long>> _sseChannels = [];
    private readonly object _sseChannelsLock = new();

    public VoxelForgeMcpSession(EditorConfigState config, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var model = new VoxelModel(loggerFactory.CreateLogger<VoxelModel>())
        {
            GridHint = config.DefaultGridHint,
        };
        var labels = new LabelIndex(loggerFactory.CreateLogger<LabelIndex>());
        var document = new EditorDocumentState(model, labels);
        var session = new EditorSessionState();
        var events = new ApplicationEventDispatcher();
        var undoHistory = new UndoHistoryState(config.MaxUndoDepth);
        var undoHistoryService = new UndoHistoryService(loggerFactory.CreateLogger<UndoHistoryService>());
        var undoStack = new UndoStack(undoHistory, undoHistoryService, events);
        var referenceModels = new ReferenceModelState();
        var referenceImages = new ReferenceImageState();

        Workspace = new VoxelForgeWorkspaceState(
            document,
            session,
            undoHistory,
            undoStack,
            events,
            referenceModels,
            referenceImages);

        CommandContext = new CommandContext
        {
            Document = Workspace.Document,
            UndoStack = Workspace.UndoStack,
            Events = Workspace.Events,
            Mode = ExecutionMode.Headless,
        };

        // Subscribe to events that indicate viewer-relevant model changes.
        events.Register<VoxelModelChangedEvent>(new ViewerRevisionEventHandler(OnViewerChange));
        events.Register<PaletteChangedEvent>(new ViewerRevisionEventHandler(OnViewerChange));
        events.Register<UndoHistoryChangedEvent>(new ViewerRevisionEventHandler(OnViewerChange));
        events.Register<ProjectLoadedEvent>(new ViewerRevisionEventHandler(OnViewerChange));
        events.Register<ReferenceModelChangedEvent>(new ViewerRevisionEventHandler(OnViewerChange));
    }

    /// <summary>
    /// Shared App-layer workspace state — authoritative mutable truth.
    /// </summary>
    public VoxelForgeWorkspaceState Workspace { get; }

    public EditorDocumentState Document => Workspace.Document;

    public UndoStack UndoStack => Workspace.UndoStack;

    public IEventDispatcher Events => Workspace.Events;

    public CommandContext CommandContext { get; }

    public ReferenceModelState ReferenceModels => Workspace.ReferenceModels;

    public string CurrentModelName
    {
        get => Workspace.CurrentModelName;
        set => Workspace.CurrentModelName = value;
    }

    public object SyncRoot => _syncRoot;

    /// <summary>
    /// Viewer revision backed by <see cref="VoxelForgeWorkspaceState.Revision"/>.
    /// </summary>
    public int ViewerRevision => (int)Workspace.Revision;

    /// <summary>
    /// Increment the workspace revision and notify SSE subscribers. Thread-safe.
    /// </summary>
    public void IncrementViewerRevision()
    {
        var revision = Workspace.IncrementRevision();

        // Broadcast to all active SSE subscribers (non-blocking, drop if full).
        List<Channel<long>> channels;
        lock (_sseChannelsLock) { channels = [.._sseChannels]; }
        foreach (var ch in channels)
        {
            ch.Writer.TryWrite(revision);
        }
    }

    /// <summary>
    /// Subscribe to viewer revision events via a bounded channel.
    /// Returns the reader and an unsubscribe action for cleanup.
    /// </summary>
    public (ChannelReader<long> Reader, Action Unsubscribe) SubscribeViewerEvents()
    {
        var ch = Channel.CreateBounded<long>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
        });
        lock (_sseChannelsLock) { _sseChannels.Add(ch); }
        return (ch.Reader, () =>
        {
            lock (_sseChannelsLock) { _sseChannels.Remove(ch); }
            ch.Writer.TryComplete();
        });
    }

    private void OnViewerChange()
    {
        IncrementViewerRevision();
    }

    /// <summary>
    /// Internal event handler that delegates to an action, used for revision tracking.
    /// </summary>
    private sealed class ViewerRevisionEventHandler :
        IEventHandler<VoxelModelChangedEvent>,
        IEventHandler<PaletteChangedEvent>,
        IEventHandler<UndoHistoryChangedEvent>,
        IEventHandler<ProjectLoadedEvent>,
        IEventHandler<ReferenceModelChangedEvent>
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
        public void Handle(ReferenceModelChangedEvent e) => _onEvent();
    }
}
