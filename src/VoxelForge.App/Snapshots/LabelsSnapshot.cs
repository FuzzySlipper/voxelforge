namespace VoxelForge.App.Snapshots;

/// <summary>
/// Renderer-neutral snapshot of region/label state.
/// Summarizes labeled regions without exposing the mutable <see cref="Core.LabelIndex"/>.
/// </summary>
public sealed class LabelsSnapshot
{
    /// <summary>
    /// Region entries ordered by ID.
    /// </summary>
    public required IReadOnlyList<RegionSnapshot> Regions { get; init; }

    /// <summary>
    /// Total number of labeled voxels (may overlap with region membership).
    /// </summary>
    public required int LabeledVoxelCount { get; init; }
}

/// <summary>
/// Renderer-neutral snapshot of a labeled region.
/// </summary>
public sealed class RegionSnapshot
{
    /// <summary>
    /// Region identifier.
    /// </summary>
    public required string RegionId { get; init; }

    /// <summary>
    /// Human-readable region name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Number of voxels in the region.
    /// </summary>
    public required int VoxelCount { get; init; }

    /// <summary>
    /// Parent region ID, or null if the region is a root.
    /// </summary>
    public string? ParentId { get; init; }
}