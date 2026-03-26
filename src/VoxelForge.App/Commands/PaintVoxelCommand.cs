using VoxelForge.Core;

namespace VoxelForge.App.Commands;

public sealed class PaintVoxelCommand : IEditorCommand
{
    private readonly VoxelModel _model;
    private readonly Point3 _pos;
    private readonly byte _newIndex;
    private readonly byte? _oldValue;

    public string Description => $"Paint voxel at {_pos}";

    public PaintVoxelCommand(VoxelModel model, Point3 pos, byte newIndex)
    {
        _model = model;
        _pos = pos;
        _newIndex = newIndex;
        _oldValue = model.GetVoxel(pos);
    }

    public void Execute()
    {
        if (_oldValue.HasValue)
            _model.SetVoxel(_pos, _newIndex);
    }

    public void Undo()
    {
        if (_oldValue.HasValue)
            _model.SetVoxel(_pos, _oldValue.Value);
    }
}
