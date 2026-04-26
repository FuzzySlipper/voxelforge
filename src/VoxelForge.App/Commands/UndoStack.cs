using Microsoft.Extensions.Logging;

namespace VoxelForge.App.Commands;

public sealed class UndoStack
{
    private readonly UndoHistoryState _history;
    private readonly ILogger<UndoStack> _logger;

    public event Action? StateChanged;

    public bool CanUndo => _history.UndoCommands.Count > 0;
    public bool CanRedo => _history.RedoCommands.Count > 0;

    public UndoStack(UndoHistoryState history, ILogger<UndoStack> logger)
    {
        _history = history;
        _logger = logger;
    }

    public void Execute(IEditorCommand cmd)
    {
        cmd.Execute();
        _history.UndoCommands.AddLast(cmd);
        _history.RedoCommands.Clear();

        if (_history.UndoCommands.Count > _history.MaxDepth)
            _history.UndoCommands.RemoveFirst();

        _logger.LogDebug("Executed: {Description}", cmd.Description);
        StateChanged?.Invoke();
    }

    public void Undo()
    {
        if (_history.UndoCommands.Count == 0) return;

        var cmd = _history.UndoCommands.Last!.Value;
        _history.UndoCommands.RemoveLast();
        cmd.Undo();
        _history.RedoCommands.Push(cmd);

        _logger.LogDebug("Undo: {Description}", cmd.Description);
        StateChanged?.Invoke();
    }

    public void Redo()
    {
        if (_history.RedoCommands.Count == 0) return;

        var cmd = _history.RedoCommands.Pop();
        cmd.Execute();
        _history.UndoCommands.AddLast(cmd);

        _logger.LogDebug("Redo: {Description}", cmd.Description);
        StateChanged?.Invoke();
    }
}
