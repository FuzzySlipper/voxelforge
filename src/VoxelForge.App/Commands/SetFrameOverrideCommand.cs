using VoxelForge.Core;

namespace VoxelForge.App.Commands;

public sealed class SetFrameOverrideCommand : IEditorCommand
{
    private readonly AnimationClip _clip;
    private readonly int _frameIndex;
    private readonly Point3 _pos;
    private readonly byte? _newValue;
    private readonly byte? _oldValue;
    private readonly bool _hadOverride;

    public string Description => $"Set frame {_frameIndex} override at {_pos}";

    public SetFrameOverrideCommand(AnimationClip clip, int frameIndex, Point3 pos, byte? newValue)
    {
        _clip = clip;
        _frameIndex = frameIndex;
        _pos = pos;
        _newValue = newValue;
        _hadOverride = clip.Frames[frameIndex].VoxelOverrides.TryGetValue(pos, out var old);
        _oldValue = old;
    }

    public void Execute() => _clip.SetFrameOverride(_frameIndex, _pos, _newValue);

    public void Undo()
    {
        if (_hadOverride)
            _clip.SetFrameOverride(_frameIndex, _pos, _oldValue);
        else
            _clip.ClearFrameOverride(_frameIndex, _pos);
    }
}
