using Microsoft.Extensions.Logging;
using VoxelForge.App.Events;

namespace VoxelForge.App.Commands;

public sealed class UndoStack
{
    private readonly UndoHistoryState _history;
    private readonly ILogger<UndoStack> _logger;
    private readonly IEventPublisher _events;

    public bool CanUndo => _history.UndoCommands.Count > 0;
    public bool CanRedo => _history.RedoCommands.Count > 0;

    public UndoStack(UndoHistoryState history, ILogger<UndoStack> logger, IEventPublisher events)
    {
        _history = history;
        _logger = logger;
        _events = events;
    }

    public void Execute(IEditorCommand cmd)
    {
        cmd.Execute();
        _history.UndoCommands.AddLast(cmd);
        _history.RedoCommands.Clear();

        if (_history.UndoCommands.Count > _history.MaxDepth)
            _history.UndoCommands.RemoveFirst();

        _logger.LogDebug("Executed: {Description}", cmd.Description);
        PublishChanged(UndoHistoryChangeKind.Executed, cmd.Description);
    }

    public void Undo()
    {
        if (_history.UndoCommands.Count == 0) return;

        var cmd = _history.UndoCommands.Last!.Value;
        _history.UndoCommands.RemoveLast();
        cmd.Undo();
        _history.RedoCommands.Push(cmd);

        _logger.LogDebug("Undo: {Description}", cmd.Description);
        PublishChanged(UndoHistoryChangeKind.Undone, cmd.Description);
    }

    public void Redo()
    {
        if (_history.RedoCommands.Count == 0) return;

        var cmd = _history.RedoCommands.Pop();
        cmd.Execute();
        _history.UndoCommands.AddLast(cmd);

        _logger.LogDebug("Redo: {Description}", cmd.Description);
        PublishChanged(UndoHistoryChangeKind.Redone, cmd.Description);
    }

    private void PublishChanged(UndoHistoryChangeKind kind, string commandDescription)
    {
        _events.Publish(new UndoHistoryChangedEvent(kind, commandDescription, CanUndo, CanRedo));
    }
}
