namespace VoxelForge.Core;

/// <summary>
/// Maps byte indices to material definitions. Index 0 is reserved (air/empty).
/// </summary>
public sealed class Palette
{
    private readonly Dictionary<byte, MaterialDef> _entries = [];

    /// <summary>
    /// Set or replace a palette entry. Index 0 is reserved and will be ignored.
    /// </summary>
    public void Set(byte index, MaterialDef def)
    {
        if (index == 0) return;
        _entries[index] = def;
    }

    public MaterialDef? Get(byte index)
    {
        return _entries.GetValueOrDefault(index);
    }

    public bool Contains(byte index) => _entries.ContainsKey(index);

    public int Count => _entries.Count;

    public IReadOnlyDictionary<byte, MaterialDef> Entries => _entries;
}
