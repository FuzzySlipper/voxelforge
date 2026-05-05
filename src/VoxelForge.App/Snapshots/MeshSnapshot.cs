namespace VoxelForge.App.Snapshots;

/// <summary>
/// Renderer-neutral snapshot of mesh geometry produced by a mesher.
/// Contains flat vertex and index buffers suitable for upload to any GPU API
/// without leaking FNA/MonoGame or rendering framework types.
/// </summary>
public sealed class MeshSnapshot
{
    /// <summary>
    /// Flat array of vertex positions: [x0, y0, z0, x1, y1, z1, ...].
    /// Length is always a multiple of 3.
    /// </summary>
    public required float[] Positions { get; init; }

    /// <summary>
    /// Flat array of vertex normals: [nx0, ny0, nz0, nx1, ny1, nz1, ...].
    /// Length matches <see cref="Positions"/> length exactly.
    /// </summary>
    public required float[] Normals { get; init; }

    /// <summary>
    /// Flat array of vertex colors: [r0, g0, b0, a0, r1, g1, b1, a1, ...].
    /// Length is (<see cref="Positions"/>.Length / 3) * 4.
    /// </summary>
    public required byte[] Colors { get; init; }

    /// <summary>
    /// Per-vertex palette indices. One entry per vertex (length = Positions.Length / 3).
    /// Null if palette mapping is not included.
    /// </summary>
    public byte[]? PaletteIndices { get; init; }

    /// <summary>
    /// Triangle index buffer. Indices reference vertices by index.
    /// Length is always a multiple of 3.
    /// </summary>
    public required int[] Indices { get; init; }

    /// <summary>
    /// Axis-aligned bounding box, or null if the model is empty.
    /// </summary>
    public BoundsSnapshot? Bounds { get; init; }

    /// <summary>
    /// Total vertex count (convenience for Positions.Length / 3).
    /// </summary>
    public int VertexCount => Positions.Length / 3;

    /// <summary>
    /// Total triangle count.
    /// </summary>
    public int TriangleCount => Indices.Length / 3;
}

/// <summary>
/// Renderer-neutral axis-aligned bounding box.
/// </summary>
public sealed class BoundsSnapshot
{
    public required int MinX { get; init; }
    public required int MinY { get; init; }
    public required int MinZ { get; init; }
    public required int MaxX { get; init; }
    public required int MaxY { get; init; }
    public required int MaxZ { get; init; }
}