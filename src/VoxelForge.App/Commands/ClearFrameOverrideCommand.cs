using VoxelForge.Core;

namespace VoxelForge.App.Commands;

public sealed class ClearFrameOverrideCommand : IEditorCommand
{
    private readonly AnimationClip _clip;
    private readonly int _frameIndex;
    private readonly Point3 _pos;
    private readonly byte? _oldValue;
    private readonly bool _hadOverride;

    public string Description => $"Clear frame {_frameIndex} override at {_pos}";

    public ClearFrameOverrideCommand(AnimationClip clip, int frameIndex, Point3 pos)
    {
        _clip = clip;
        _frameIndex = frameIndex;
        _pos = pos;
        _hadOverride = clip.Frames[frameIndex].VoxelOverrides.TryGetValue(pos, out var old);
        _oldValue = old;
    }

    public void Execute() => _clip.ClearFrameOverride(_frameIndex, _pos);

    public void Undo()
    {
        if (_hadOverride)
            _clip.SetFrameOverride(_frameIndex, _pos, _oldValue);
    }
}
