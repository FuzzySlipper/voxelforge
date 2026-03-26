namespace VoxelForge.Core;

/// <summary>
/// A single animation frame stored as deltas from the base model.
/// Only overridden voxels/labels are stored.
/// </summary>
public sealed class AnimationFrame
{
    /// <summary>
    /// Voxel overrides for this frame. A byte value sets the palette index;
    /// null removes the voxel (override to air) even if it exists in the base.
    /// </summary>
    public Dictionary<Point3, byte?> VoxelOverrides { get; } = [];

    /// <summary>
    /// Optional per-frame label overrides. Null removes the label for that position.
    /// </summary>
    public Dictionary<Point3, RegionId?> LabelOverrides { get; } = [];

    /// <summary>
    /// Optional per-frame duration override in seconds. Null uses the clip's default frame rate.
    /// </summary>
    public float? Duration { get; set; }
}
