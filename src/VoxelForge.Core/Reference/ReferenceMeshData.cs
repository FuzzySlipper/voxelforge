namespace VoxelForge.Core.Reference;

/// <summary>
/// A single vertex in a reference mesh. Plain floats — no engine types.
/// </summary>
public readonly record struct ReferenceVertex(
    float PosX, float PosY, float PosZ,
    float NormX, float NormY, float NormZ,
    byte R, byte G, byte B, byte A);

/// <summary>
/// A triangle mesh extracted from a reference model file. Engine-agnostic.
/// </summary>
public sealed class ReferenceMeshData
{
    public required ReferenceVertex[] Vertices { get; init; }
    public required int[] Indices { get; init; }
    public string MaterialName { get; init; } = "default";
}

/// <summary>
/// Render mode for displaying reference models.
/// </summary>
public enum ReferenceRenderMode
{
    Solid,
    Wireframe,
    Transparent,
}

/// <summary>
/// A loaded reference model with mesh data and transform.
/// </summary>
public sealed class ReferenceModelData
{
    public required string FilePath { get; init; }
    public required string Format { get; init; }
    public required List<ReferenceMeshData> Meshes { get; init; }
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    public float RotationX { get; set; }
    public float RotationY { get; set; }
    public float RotationZ { get; set; }
    public float Scale { get; set; } = 1f;
    public bool IsVisible { get; set; } = true;
    public ReferenceRenderMode RenderMode { get; set; } = ReferenceRenderMode.Solid;

    public int TotalVertices => Meshes.Sum(m => m.Vertices.Length);
    public int TotalTriangles => Meshes.Sum(m => m.Indices.Length / 3);
}
