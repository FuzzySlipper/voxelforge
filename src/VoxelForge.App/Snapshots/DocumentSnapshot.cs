namespace VoxelForge.App.Snapshots;

/// <summary>
/// Renderer-neutral snapshot of editor document state:
/// model metadata, palette, labels, and animation clip summaries.
/// Does NOT include the mutable model object itself — use <see cref="MeshSnapshotService"/>
/// for geometry and <see cref="PaletteSnapshotService"/> for material definitions.
/// </summary>
public sealed class DocumentSnapshot
{
    /// <summary>
    /// Identifier for the open document (typically the file name or "untitled").
    /// </summary>
    public required string ModelId { get; init; }

    /// <summary>
    /// Total number of non-air voxels in the model.
    /// </summary>
    public required int VoxelCount { get; init; }

    /// <summary>
    /// Axis-aligned bounds of the model, or null if empty.
    /// </summary>
    public BoundsSnapshot? Bounds { get; init; }

    /// <summary>
    /// Advisory grid resolution hint.
    /// </summary>
    public required int GridHint { get; init; }

    /// <summary>
    /// Number of animation clips.
    /// </summary>
    public required int ClipCount { get; init; }

    /// <summary>
    /// Whether the document has unsaved changes.
    /// </summary>
    public required bool IsDirty { get; init; }
}