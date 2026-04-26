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
    }

    public EditorDocumentState Document { get; }

    public UndoStack UndoStack { get; }

    public IEventDispatcher Events { get; }

    public CommandContext CommandContext { get; }

    public object SyncRoot => _syncRoot;
}
