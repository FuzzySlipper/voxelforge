namespace VoxelForge.App.Snapshots;

/// <summary>
/// Renderer-neutral snapshot of the current editor session choices
/// (tool, palette, selection) that a renderer needs for UI display,
/// without exposing mutable state objects directly.
/// </summary>
public sealed class SessionSnapshot
{
    /// <summary>
    /// Name of the currently active tool (e.g., "Place", "Remove", "Paint", "Select", "Fill", "Label").
    /// </summary>
    public required string ActiveTool { get; init; }

    /// <summary>
    /// Currently active palette index (1–255). 0 is reserved for air.
    /// </summary>
    public required byte ActivePaletteIndex { get; init; }

    /// <summary>
    /// Currently selected voxels, or null/empty if none selected.
    /// </summary>
    public required IReadOnlyList<Point3Snapshot> SelectedVoxels { get; init; }

    /// <summary>
    /// Currently active region ID, or null if no region is active.
    /// </summary>
    public string? ActiveRegionId { get; init; }

    /// <summary>
    /// Currently active animation frame index, or -1 for the base model.
    /// </summary>
    public required int ActiveFrameIndex { get; init; }
}

/// <summary>
/// Renderer-neutral snapshot of selection state.
/// </summary>
public sealed class SelectionSnapshot
{
    /// <summary>
    /// Selected voxel positions.
    /// </summary>
    public required IReadOnlyList<Point3Snapshot> Voxels { get; init; }

    /// <summary>
    /// Currently active region, or null.
    /// </summary>
    public string? ActiveRegionId { get; init; }

    /// <summary>
    /// Bounds of the selection, or null if no voxels are selected.
    /// </summary>
    public BoundsSnapshot? Bounds { get; init; }
}