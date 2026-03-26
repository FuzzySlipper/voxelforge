using Microsoft.Extensions.Logging;

namespace VoxelForge.App.Commands;

public sealed class UndoStack
{
    private readonly LinkedList<IEditorCommand> _undoStack = new();
    private readonly Stack<IEditorCommand> _redoStack = new();
    private readonly int _maxDepth;
    private readonly ILogger<UndoStack> _logger;

    public event Action? StateChanged;

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public UndoStack(int maxDepth, ILogger<UndoStack> logger)
    {
        _maxDepth = maxDepth;
        _logger = logger;
    }

    public void Execute(IEditorCommand cmd)
    {
        cmd.Execute();
        _undoStack.AddLast(cmd);
        _redoStack.Clear();

        if (_undoStack.Count > _maxDepth)
            _undoStack.RemoveFirst();

        _logger.LogDebug("Executed: {Description}", cmd.Description);
        StateChanged?.Invoke();
    }

    public void Undo()
    {
        if (_undoStack.Count == 0) return;

        var cmd = _undoStack.Last!.Value;
        _undoStack.RemoveLast();
        cmd.Undo();
        _redoStack.Push(cmd);

        _logger.LogDebug("Undo: {Description}", cmd.Description);
        StateChanged?.Invoke();
    }

    public void Redo()
    {
        if (_redoStack.Count == 0) return;

        var cmd = _redoStack.Pop();
        cmd.Execute();
        _undoStack.AddLast(cmd);

        _logger.LogDebug("Redo: {Description}", cmd.Description);
        StateChanged?.Invoke();
    }
}
