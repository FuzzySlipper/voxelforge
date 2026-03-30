namespace VoxelForge.Core.Reference;

/// <summary>
/// A single bone in a skeleton hierarchy. Engine-agnostic (plain floats).
/// </summary>
public sealed class Bone
{
    public required string Name { get; init; }
    public int ParentIndex { get; init; } = -1;

    /// <summary>
    /// Inverse bind matrix (4x4, row-major) — transforms from mesh space to bone-local space.
    /// Layout: [m11, m12, m13, m14, m21, m22, m23, m24, m31, m32, m33, m34, m41, m42, m43, m44]
    /// </summary>
    public required float[] InverseBindMatrix { get; init; }

    /// <summary>
    /// Local transform relative to parent in bind pose (4x4, row-major).
    /// </summary>
    public float[] LocalBindTransform { get; init; } = Identity();

    private static float[] Identity() =>
    [
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 1, 0,
        0, 0, 0, 1,
    ];
}

/// <summary>
/// A skeleton: ordered list of bones where each bone's parent has a lower index.
/// </summary>
public sealed class Skeleton
{
    public required List<Bone> Bones { get; init; }

    /// <summary>
    /// Index of the root bone (typically 0).
    /// </summary>
    public int RootIndex { get; init; }

    public int BoneCount => Bones.Count;

    public int FindBoneIndex(string name)
    {
        for (int i = 0; i < Bones.Count; i++)
            if (Bones[i].Name == name)
                return i;
        return -1;
    }
}
