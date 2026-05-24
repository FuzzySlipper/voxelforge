namespace VoxelForge.App.Snapshots;

/// <summary>
/// Aggregate renderer-neutral snapshot of the full editor state.
/// Combines document, session, palette, mesh, labels, undo history,
/// and diagnostics into a single immutable snapshot suitable for
/// serialization to any renderer (WebGL, Electron, headless).
/// <para>
/// This type does not hold mutable state or references to engine types.
/// Consumers should treat it as read-only; mutations flow through
/// <c>IEditorCommand</c> and <c>UndoStack</c>.
/// </para>
/// </summary>
public sealed class EditorSnapshot
{
    public required DocumentSnapshot Document { get; init; }
    public required SessionSnapshot Session { get; init; }
    public required PaletteSnapshot Palette { get; init; }
    public required MeshSnapshot Mesh { get; init; }
    public required LabelsSnapshot Labels { get; init; }
    public required UndoHistorySnapshot UndoHistory { get; init; }
    public required DiagnosticsSnapshot Diagnostics { get; init; }
}