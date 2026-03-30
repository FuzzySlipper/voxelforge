namespace VoxelForge.Core.Reference;

/// <summary>
/// A keyframe for a single bone: position, rotation (quaternion), and scale at a point in time.
/// </summary>
public readonly record struct BoneKeyframe(
    float Time,
    float PosX, float PosY, float PosZ,
    float RotX, float RotY, float RotZ, float RotW,
    float ScaleX, float ScaleY, float ScaleZ);

/// <summary>
/// Animation data for one bone within a clip — a sorted list of keyframes.
/// </summary>
public sealed class BoneAnimationChannel
{
    public required int BoneIndex { get; init; }
    public required string BoneName { get; init; }
    public required BoneKeyframe[] Keyframes { get; init; }

    /// <summary>
    /// Find the two keyframes surrounding the given time for interpolation.
    /// Returns the index of the keyframe at or before <paramref name="time"/>.
    /// </summary>
    public int FindKeyframeIndex(float time)
    {
        for (int i = Keyframes.Length - 2; i >= 0; i--)
            if (Keyframes[i].Time <= time)
                return i;
        return 0;
    }
}

/// <summary>
/// A named animation clip containing per-bone keyframe channels.
/// </summary>
public sealed class SkeletalAnimationClip
{
    public required string Name { get; init; }

    /// <summary>Duration in seconds.</summary>
    public required float Duration { get; init; }

    /// <summary>Ticks per second from the source file (used for time conversion).</summary>
    public float TicksPerSecond { get; init; } = 25f;

    /// <summary>One channel per animated bone.</summary>
    public required List<BoneAnimationChannel> Channels { get; init; }

    /// <summary>
    /// Find the channel for a given bone index, or null if this bone has no animation in this clip.
    /// </summary>
    public BoneAnimationChannel? FindChannel(int boneIndex)
    {
        foreach (var ch in Channels)
            if (ch.BoneIndex == boneIndex)
                return ch;
        return null;
    }
}
