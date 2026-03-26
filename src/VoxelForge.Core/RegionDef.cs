namespace VoxelForge.Core;

/// <summary>
/// Defines a labeled region — a named group of voxels with optional parent for hierarchy.
/// This is the persisted representation (source of truth on disk).
/// </summary>
public sealed class RegionDef
{
    public required RegionId Id { get; init; }
    public required string Name { get; init; }
    public HashSet<Point3> Voxels { get; init; } = [];
    public RegionId? ParentId { get; init; }
    public Dictionary<string, string> Properties { get; init; } = [];
}
