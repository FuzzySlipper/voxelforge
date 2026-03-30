using VoxelForge.Core;

namespace VoxelForge.App.Commands;

/// <summary>
/// Consolidates palette entries: reassigns voxels to surviving indices,
/// optionally recolors surviving entries, and removes freed entries.
/// Supports undo.
/// </summary>
public sealed class PaletteReduceCommand : IEditorCommand
{
    private readonly VoxelModel _model;
    private readonly List<(byte Index, MaterialDef OldDef, MaterialDef? NewDef)> _paletteChanges;
    private readonly List<(Point3 Pos, byte OldIndex, byte NewIndex)> _voxelChanges;

    public string Description { get; }

    /// <param name="paletteChanges">
    /// Per palette index: OldDef is the original entry. NewDef is the replacement,
    /// or null if the entry should be removed.
    /// </param>
    public PaletteReduceCommand(
        VoxelModel model,
        string description,
        List<(byte Index, MaterialDef OldDef, MaterialDef? NewDef)> paletteChanges,
        List<(Point3 Pos, byte OldIndex, byte NewIndex)> voxelChanges)
    {
        _model = model;
        Description = description;
        _paletteChanges = paletteChanges;
        _voxelChanges = voxelChanges;
    }

    public void Execute()
    {
        foreach (var (pos, _, newIdx) in _voxelChanges)
            _model.SetVoxel(pos, newIdx);
        foreach (var (idx, _, newDef) in _paletteChanges)
        {
            if (newDef != null)
                _model.Palette.Set(idx, newDef);
            else
                _model.Palette.Remove(idx);
        }
    }

    public void Undo()
    {
        foreach (var (pos, oldIdx, _) in _voxelChanges)
            _model.SetVoxel(pos, oldIdx);
        foreach (var (idx, oldDef, _) in _paletteChanges)
            _model.Palette.Set(idx, oldDef);
    }
}
