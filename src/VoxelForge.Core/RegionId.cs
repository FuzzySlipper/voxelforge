namespace VoxelForge.Core;

/// <summary>
/// Typed identifier for a labeled region.
/// </summary>
public readonly record struct RegionId(string Value)
{
    public override string ToString() => Value;
}
