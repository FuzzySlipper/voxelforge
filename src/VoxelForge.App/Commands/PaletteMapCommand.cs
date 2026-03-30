using VoxelForge.Core;

namespace VoxelForge.App.Commands;

/// <summary>
/// Replaces colors on matching palette entries. Supports undo.
/// </summary>
public sealed class PaletteMapCommand : IEditorCommand
{
    private readonly Palette _palette;
    private readonly List<(byte Index, MaterialDef Old, MaterialDef New)> _changes;

    public string Description { get; }

    public PaletteMapCommand(Palette palette, List<(byte Index, MaterialDef Old, MaterialDef New)> changes)
    {
        _palette = palette;
        _changes = changes;
        Description = $"Palette map ({_changes.Count} entries)";
    }

    public void Execute()
    {
        foreach (var (idx, _, newDef) in _changes)
            _palette.Set(idx, newDef);
    }

    public void Undo()
    {
        foreach (var (idx, oldDef, _) in _changes)
            _palette.Set(idx, oldDef);
    }
}
