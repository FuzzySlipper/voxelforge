using VoxelForge.Core;

namespace VoxelForge.App.Commands;

public sealed class RemoveVoxelCommand : IEditorCommand
{
    private readonly VoxelModel _model;
    private readonly Point3 _pos;
    private readonly byte? _oldValue;

    public string Description => $"Remove voxel at {_pos}";

    public RemoveVoxelCommand(VoxelModel model, Point3 pos)
    {
        _model = model;
        _pos = pos;
        _oldValue = model.GetVoxel(pos);
    }

    public void Execute() => _model.RemoveVoxel(_pos);

    public void Undo()
    {
        if (_oldValue.HasValue)
            _model.SetVoxel(_pos, _oldValue.Value);
    }
}
