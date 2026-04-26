using VoxelForge.Core;

namespace VoxelForge.App.Commands;

/// <summary>
/// Sets or replaces a palette material entry, preserving the previous entry for undo.
/// </summary>
public sealed class SetPaletteMaterialCommand : IEditorCommand
{
    private readonly Palette _palette;
    private readonly byte _index;
    private readonly MaterialDef _newDef;
    private readonly MaterialDef? _oldDef;
    private readonly bool _hadOldDef;

    public string Description { get; }

    public SetPaletteMaterialCommand(Palette palette, byte index, MaterialDef newDef, string description)
    {
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(newDef);
        ArgumentNullException.ThrowIfNull(description);

        if (index == 0)
            throw new ArgumentOutOfRangeException(nameof(index), "Palette index 0 is reserved for air.");

        _palette = palette;
        _index = index;
        _newDef = CloneMaterial(newDef);
        var oldDef = palette.Get(index);
        _hadOldDef = oldDef is not null;
        _oldDef = oldDef is not null ? CloneMaterial(oldDef) : null;
        Description = description;
    }

    public void Execute()
    {
        _palette.Set(_index, CloneMaterial(_newDef));
    }

    public void Undo()
    {
        if (_hadOldDef && _oldDef is not null)
            _palette.Set(_index, CloneMaterial(_oldDef));
        else
            _palette.Remove(_index);
    }

    internal static MaterialDef CloneMaterial(MaterialDef material)
    {
        return new MaterialDef
        {
            Name = material.Name,
            Color = material.Color,
            Metadata = new Dictionary<string, string>(material.Metadata),
        };
    }
}
