using VoxelForge.Core;

namespace VoxelForge.App.Commands;

public sealed class SetGridHintCommand : IEditorCommand
{
    private readonly VoxelModel _model;
    private readonly int _newSize;
    private readonly int _oldSize;

    public string Description => $"Set grid hint to {_newSize}";

    public SetGridHintCommand(VoxelModel model, int newSize)
    {
        _model = model;
        _newSize = newSize;
        _oldSize = model.GridHint;
    }

    public void Execute()
    {
        _model.GridHint = _newSize;
    }

    public void Undo()
    {
        _model.GridHint = _oldSize;
    }
}
