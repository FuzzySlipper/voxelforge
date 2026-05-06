namespace VoxelForge.App.Snapshots;

/// <summary>
/// Renderer-neutral snapshot of undo/redo history state.
/// </summary>
public sealed class UndoHistorySnapshot
{
    /// <summary>
    /// Whether an undo operation is available.
    /// </summary>
    public required bool CanUndo { get; init; }

    /// <summary>
    /// Whether a redo operation is available.
    /// </summary>
    public required bool CanRedo { get; init; }

    /// <summary>
    /// Number of undo steps available.
    /// </summary>
    public required int UndoDepth { get; init; }

    /// <summary>
    /// Number of redo steps available.
    /// </summary>
    public required int RedoDepth { get; init; }

    /// <summary>
    /// Description of the most recently executed command, or null/empty.
    /// </summary>
    public string? LastCommandDescription { get; init; }
}