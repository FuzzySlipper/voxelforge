using Microsoft.Extensions.Logging;
using VoxelForge.App.Events;

namespace VoxelForge.App.Commands;

/// <summary>
/// Compatibility facade over explicit undo history state and UndoHistoryService.
/// </summary>
public sealed class UndoStack
{
    private readonly UndoHistoryState _history;
    private readonly UndoHistoryService _service;
    private readonly IEventPublisher _events;

    public UndoStack(UndoHistoryState history, ILogger<UndoStack> logger, IEventPublisher events)
        : this(history, new UndoHistoryService(logger), events)
    {
    }

    public UndoStack(UndoHistoryState history, UndoHistoryService service, IEventPublisher events)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(events);

        _history = history;
        _service = service;
        _events = events;
    }

    public UndoHistoryState History => _history;

    public UndoHistoryService Service => _service;

    public bool CanUndo => _service.CanUndo(_history);
    public bool CanRedo => _service.CanRedo(_history);

    public void Execute(IEditorCommand cmd)
    {
        _service.Execute(_history, _events, cmd);
    }

    public void Undo()
    {
        _service.Undo(_history, _events);
    }

    public void Redo()
    {
        _service.Redo(_history, _events);
    }
}
