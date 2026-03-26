using Microsoft.Extensions.Logging;

namespace VoxelForge.Core;

/// <summary>
/// Frame-swap animation clip. Stores a shared base model and per-frame overrides.
/// Each frame is a complete voxel state computed as base + overrides.
/// </summary>
public sealed class AnimationClip
{
    private readonly List<AnimationFrame> _frames = [];
    private readonly ILogger<AnimationClip> _logger;

    public required string Name { get; init; }
    public VoxelModel Base { get; }
    public int FrameRate { get; set; } = 12;
    public IReadOnlyList<AnimationFrame> Frames => _frames;

    public AnimationClip(VoxelModel baseModel, ILogger<AnimationClip> logger)
    {
        Base = baseModel;
        _logger = logger;
    }

    /// <summary>
    /// Resolve a frame into a complete VoxelModel by applying overrides to the base.
    /// </summary>
    public VoxelModel ResolveFrame(int frameIndex, ILogger<VoxelModel> modelLogger)
    {
        var frame = _frames[frameIndex];
        var resolved = new VoxelModel(modelLogger) { GridHint = Base.GridHint };

        // Copy palette
        foreach (var (idx, mat) in Base.Palette.Entries)
            resolved.Palette.Set(idx, mat);

        // Copy base voxels
        foreach (var (pos, val) in Base.Voxels)
            resolved.SetVoxel(pos, val);

        // Apply overrides
        foreach (var (pos, val) in frame.VoxelOverrides)
        {
            if (val.HasValue)
                resolved.SetVoxel(pos, val.Value);
            else
                resolved.RemoveVoxel(pos);
        }

        return resolved;
    }

    public void AddFrame()
    {
        _frames.Add(new AnimationFrame());
        _logger.LogTrace("AddFrame (now {Count} frames)", _frames.Count);
    }

    public void RemoveFrame(int index)
    {
        _frames.RemoveAt(index);
        _logger.LogTrace("RemoveFrame({Index}), now {Count} frames", index, _frames.Count);
    }

    public void SetFrameOverride(int frameIndex, Point3 pos, byte? value)
    {
        _frames[frameIndex].VoxelOverrides[pos] = value;
    }

    public void ClearFrameOverride(int frameIndex, Point3 pos)
    {
        _frames[frameIndex].VoxelOverrides.Remove(pos);
    }

    public int GetOverrideCount(int frameIndex) => _frames[frameIndex].VoxelOverrides.Count;
}
