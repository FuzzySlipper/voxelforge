namespace VoxelForge.App.Commands;

/// <summary>
/// Durable mutable state for undo and redo history.
/// </summary>
public sealed class UndoHistoryState
{
    public UndoHistoryState(int maxDepth)
    {
        if (maxDepth < 1)
            throw new ArgumentOutOfRangeException(nameof(maxDepth), "Undo history depth must be at least 1.");

        MaxDepth = maxDepth;
    }

    public int MaxDepth { get; }

    public int UndoCount => UndoCommands.Count;

    public int RedoCount => RedoCommands.Count;

    internal LinkedList<IEditorCommand> UndoCommands { get; } = new();
    internal Stack<IEditorCommand> RedoCommands { get; } = new();
}
