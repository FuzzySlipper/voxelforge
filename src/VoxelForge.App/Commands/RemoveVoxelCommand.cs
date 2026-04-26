using VoxelForge.Core;

namespace VoxelForge.App.Commands;

public sealed class RemoveVoxelCommand : IEditorCommand
{
    private readonly VoxelModel _model;
    private readonly LabelIndex? _labels;
    private readonly Point3 _pos;
    private readonly byte? _oldValue;
    private readonly RegionId? _oldRegion;

    public string Description => $"Remove voxel at {_pos}";

    public RemoveVoxelCommand(VoxelModel model, Point3 pos)
        : this(model, pos, null)
    {
    }

    public RemoveVoxelCommand(VoxelModel model, LabelIndex labels, Point3 pos)
        : this(model, pos, labels)
    {
    }

    private RemoveVoxelCommand(VoxelModel model, Point3 pos, LabelIndex? labels)
    {
        ArgumentNullException.ThrowIfNull(model);

        _model = model;
        _labels = labels;
        _pos = pos;
        _oldValue = model.GetVoxel(pos);
        _oldRegion = _oldValue.HasValue ? labels?.GetRegion(pos) : null;
    }

    public void Execute()
    {
        _model.RemoveVoxel(_pos);
        _labels?.RemoveFromRegion(_pos);
    }

    public void Undo()
    {
        if (!_oldValue.HasValue)
            return;

        _model.SetVoxel(_pos, _oldValue.Value);
        if (_oldRegion.HasValue)
            _labels?.AssignRegion(_oldRegion.Value, [_pos]);
    }
}
