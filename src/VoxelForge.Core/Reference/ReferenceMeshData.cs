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

    // ── Source tracking (set during loading) ──

    /// <summary>
    /// Origin label for diffuse texture: "assimp" (imported via AssimpNet),
    /// "unity_sidecar" (resolved from Unity .mat files), or null/empty.
    /// </summary>
    public string? DiffuseTextureSource { get; init; }

    /// <summary>
    /// Origin label for emissive texture: "unity_sidecar" or null/empty.
    /// </summary>
    public string? EmissiveTextureSource { get; init; }

    // ── Session-only manual texture overrides (mutable, survive only for this MCP session) ──

    /// <summary>Manual override for diffuse/base-color texture. Null if not set.</summary>
    public string? ManualDiffuseOverridePath { get; set; }

    /// <summary>Manual override for normal map texture. Null if not set.</summary>
    public string? ManualNormalOverridePath { get; set; }

    /// <summary>Manual override for emissive texture. Null if not set.</summary>
    public string? ManualEmissiveOverridePath { get; set; }

    // ── Computed helpers ──

    /// <summary>
    /// Effective diffuse texture path: manual override wins, then original import path.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? EffectiveDiffuseTexturePath => ManualDiffuseOverridePath ?? DiffuseTexturePath;

    /// <summary>
    /// Effective normal texture path: always the manual override (no import source for normals).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? EffectiveNormalTexturePath => ManualNormalOverridePath;

    /// <summary>
    /// Effective emissive texture path: manual override wins, then original import path.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? EffectiveEmissiveTexturePath => ManualEmissiveOverridePath ?? EmissiveTexturePath;

    /// <summary>
    /// Whether this mesh has non-zero UV coordinates from the source asset.
    /// Meshes without UVs cannot display textures correctly.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasUvs => Vertices.Length > 0 && Vertices.Any(v => v.U != 0f || v.V != 0f);

    /// <summary>
    /// Source label for diffuse texture diagnostics.
    /// "manual_override" when a manual override is active,
    /// "unity_sidecar" when resolved from Unity .mat,
    /// "assimp" when imported by AssimpNet,
    /// "none" when no diffuse texture is available.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string DiffuseSourceLabel
    {
        get
        {
            if (ManualDiffuseOverridePath is not null)
                return "manual_override";
            if (DiffuseTexturePath is not null)
                return string.Equals(DiffuseTextureSource, "unity_sidecar", StringComparison.OrdinalIgnoreCase)
                    ? "unity_sidecar"
                    : "assimp";
            return "none";
        }
    }
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

    /// <summary>Unity .mat sidecar processing result (optional, populated during load).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public UnityMatSidecarResult? UnitySidecarResult { get; set; }

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
