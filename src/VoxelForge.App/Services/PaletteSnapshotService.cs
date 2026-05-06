using VoxelForge.App.Snapshots;
using VoxelForge.Core;

namespace VoxelForge.App.Services;

/// <summary>
/// Stateless service that produces renderer-neutral palette/material snapshots
/// from the model's <see cref="Palette"/>. Does not hold mutable state.
/// </summary>
public sealed class PaletteSnapshotService
{
    /// <summary>
    /// Build a renderer-neutral palette snapshot from the given palette.
    /// Index 0 (air) is never included in the snapshot because
    /// <see cref="Core.Palette.Set"/> silently ignores index 0.
    /// <see cref="PaletteSnapshot.EntryCount"/> reflects the actual number of
    /// palette entries, not the full 0–255 range.
    /// </summary>
    public PaletteSnapshot BuildSnapshot(Palette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);

        var entries = new List<PaletteEntrySnapshot>(palette.Count);

        foreach (var (idx, mat) in palette.Entries)
        {
            entries.Add(new PaletteEntrySnapshot
            {
                Index = idx,
                Name = mat.Name,
                R = mat.Color.R,
                G = mat.Color.G,
                B = mat.Color.B,
                A = mat.Color.A,
            });
        }

        // Sort by palette index for deterministic output
        entries.Sort((a, b) => a.Index.CompareTo(b.Index));

        return new PaletteSnapshot
        {
            Entries = entries,
            EntryCount = palette.Count,
        };
    }
}