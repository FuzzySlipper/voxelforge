using VoxelForge.Core;

namespace VoxelForge.App.Commands;

/// <summary>
/// Shared editor command for bake operations that create palette entries
/// and reassign voxel indices. Supports undo.
/// </summary>
public sealed class VoxelBakeCommand : IEditorCommand
{
    private readonly VoxelModel _model;
    private readonly List<(byte Index, MaterialDef? OldDef, MaterialDef NewDef)> _paletteChanges;
    private readonly List<(Point3 Pos, byte OldIndex, byte NewIndex)> _voxelChanges;

    public string Description { get; }

    public VoxelBakeCommand(
        VoxelModel model,
        string description,
        List<(byte Index, MaterialDef? OldDef, MaterialDef NewDef)> paletteChanges,
        List<(Point3 Pos, byte OldIndex, byte NewIndex)> voxelChanges)
    {
        _model = model;
        Description = description;
        _paletteChanges = paletteChanges;
        _voxelChanges = voxelChanges;
    }

    public void Execute()
    {
        foreach (var (idx, _, newDef) in _paletteChanges)
            _model.Palette.Set(idx, newDef);
        foreach (var (pos, _, newIdx) in _voxelChanges)
            _model.SetVoxel(pos, newIdx);
    }

    public void Undo()
    {
        foreach (var (pos, oldIdx, _) in _voxelChanges)
            _model.SetVoxel(pos, oldIdx);
        foreach (var (idx, oldDef, _) in _paletteChanges)
        {
            if (oldDef != null)
                _model.Palette.Set(idx, oldDef);
            else
                _model.Palette.Remove(idx);
        }
    }
}
