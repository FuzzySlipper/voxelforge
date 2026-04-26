using Microsoft.Extensions.Logging;
using VoxelForge.App.Events;

namespace VoxelForge.App.Commands;

/// <summary>
/// Stateless service that applies undoable editor commands to explicit undo history state.
/// </summary>
public sealed class UndoHistoryService
{
    private readonly ILogger _logger;

    public UndoHistoryService(ILogger<UndoHistoryService> logger)
    {
        _logger = logger;
    }

    internal UndoHistoryService(ILogger logger)
    {
        _logger = logger;
    }

    public bool CanUndo(UndoHistoryState history)
    {
        ArgumentNullException.ThrowIfNull(history);
        return history.UndoCommands.Count > 0;
    }

    public bool CanRedo(UndoHistoryState history)
    {
        ArgumentNullException.ThrowIfNull(history);
        return history.RedoCommands.Count > 0;
    }

    public void Execute(UndoHistoryState history, IEventPublisher events, IEditorCommand command)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(command);

        command.Execute();
        history.UndoCommands.AddLast(command);
        history.RedoCommands.Clear();

        if (history.UndoCommands.Count > history.MaxDepth)
            history.UndoCommands.RemoveFirst();

        _logger.LogDebug("Executed: {Description}", command.Description);
        PublishChanged(history, events, UndoHistoryChangeKind.Executed, command.Description);
    }

    public void Undo(UndoHistoryState history, IEventPublisher events)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(events);

        if (history.UndoCommands.Count == 0)
            return;

        var command = history.UndoCommands.Last!.Value;
        history.UndoCommands.RemoveLast();
        command.Undo();
        history.RedoCommands.Push(command);

        _logger.LogDebug("Undo: {Description}", command.Description);
        PublishChanged(history, events, UndoHistoryChangeKind.Undone, command.Description);
    }

    public void Redo(UndoHistoryState history, IEventPublisher events)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(events);

        if (history.RedoCommands.Count == 0)
            return;

        var command = history.RedoCommands.Pop();
        command.Execute();
        history.UndoCommands.AddLast(command);

        _logger.LogDebug("Redo: {Description}", command.Description);
        PublishChanged(history, events, UndoHistoryChangeKind.Redone, command.Description);
    }

    private void PublishChanged(
        UndoHistoryState history,
        IEventPublisher events,
        UndoHistoryChangeKind kind,
        string commandDescription)
    {
        events.Publish(new UndoHistoryChangedEvent(
            kind,
            commandDescription,
            CanUndo(history),
            CanRedo(history)));
    }
}
