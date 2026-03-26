namespace VoxelForge.Core.Meshing;

/// <summary>
/// Derived mesh data from a VoxelModel. Never serialized — rebuilt on demand.
/// </summary>
public sealed class VoxelMesh
{
    public VoxelVertex[] Vertices { get; init; } = [];
    public int[] Indices { get; init; } = [];
    public (Point3 Min, Point3 Max)? Bounds { get; init; }
    public int TriangleCount => Indices.Length / 3;
}
