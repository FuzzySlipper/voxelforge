namespace VoxelForge.Core.Reference;

/// <summary>
/// A single vertex in a reference mesh. Plain floats — no engine types.
/// </summary>
public readonly record struct ReferenceVertex(
    float PosX, float PosY, float PosZ,
    float NormX, float NormY, float NormZ,
    byte R, byte G, byte B, byte A,
    float U = 0f, float V = 0f,
    int BoneIndex0 = 0, int BoneIndex1 = 0, int BoneIndex2 = 0, int BoneIndex3 = 0,
    float BoneWeight0 = 0f, float BoneWeight1 = 0f, float BoneWeight2 = 0f, float BoneWeight3 = 0f);

/// <summary>
/// A triangle mesh extracted from a reference model file. Engine-agnostic.
/// </summary>
public sealed class ReferenceMeshData
{
    public required ReferenceVertex[] Vertices { get; init; }
    public required int[] Indices { get; init; }
    public string MaterialName { get; init; } = "default";
    public string? DiffuseTexturePath { get; init; }
    public string? EmissiveTexturePath { get; init; }
    public float EmissiveBrightness { get; init; }
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

    public Skeleton? Skeleton { get; set; }
    public List<SkeletalAnimationClip>? AnimationClips { get; set; }

    // Animation playback state
    public int? ActiveClipIndex { get; set; }
    public float AnimationTime { get; set; }
    public bool IsAnimating { get; set; }
    public float AnimationSpeed { get; set; } = 1f;

    public bool HasAnimations => AnimationClips is { Count: > 0 } && Skeleton is not null;

    /// <summary>
    /// Advance the animation clock. Loops back to start when the clip ends.
    /// </summary>
    public void UpdateAnimation(float deltaSeconds)
    {
        if (!IsAnimating || ActiveClipIndex is not { } clipIdx)
            return;
        if (AnimationClips is null || clipIdx < 0 || clipIdx >= AnimationClips.Count)
            return;

        var clip = AnimationClips[clipIdx];
        AnimationTime += deltaSeconds * AnimationSpeed;

        if (clip.Duration > 0)
            AnimationTime %= clip.Duration;
    }

    public int TotalVertices => Meshes.Sum(m => m.Vertices.Length);
    public int TotalTriangles => Meshes.Sum(m => m.Indices.Length / 3);
}
