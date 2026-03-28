using System.Numerics;

namespace VoxelForge.Core.Voxelization;

/// <summary>
/// Engine-agnostic triangle mesh for voxelization. Uses System.Numerics — no FNA types.
/// </summary>
public sealed class TriangleMesh
{
    public required Vector3[] Positions { get; init; }
    public required int[] Indices { get; init; }

    /// <summary>
    /// Optional per-vertex colors, parallel to Positions. When present,
    /// voxelization interpolates colors at ray-hit points.
    /// </summary>
    public RgbaColor[]? VertexColors { get; init; }

    public int TriangleCount => Indices.Length / 3;
}

public enum VoxelizeMode
{
    Surface,
    Solid,
}
