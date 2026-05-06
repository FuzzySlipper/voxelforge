namespace VoxelForge.App.Snapshots;

/// <summary>
/// Renderer-neutral snapshot of palette/material entries.
/// Maps palette indices to name, RGBA color, and optional metadata.
/// </summary>
public sealed class PaletteSnapshot
{
    /// <summary>
    /// Palette entries sorted by index. Index 0 (air) is always excluded.
    /// </summary>
    public required IReadOnlyList<PaletteEntrySnapshot> Entries { get; init; }

    /// <summary>
    /// Total number of palette entries assigned by the model's <see cref="Core.Palette"/>.
    /// This is equal to <c>palette.Count</c> — the number of material entries that have
    /// been set on the palette, and always equals <see cref="Entries"/>.Count because
    /// <c>Palette</c> is a sparse dictionary with no gap entries.
    /// <para>
    /// Index 0 (air) is reserved and <see cref="Core.Palette.Set"/> silently ignores
    /// attempts to set index 0, so index 0 never contributes to <c>EntryCount</c>.
    /// </para>
    /// </summary>
    public required int EntryCount { get; init; }
}

/// <summary>
/// A single palette/material entry for renderer consumption.
/// </summary>
public sealed class PaletteEntrySnapshot
{
    /// <summary>
    /// Palette index (1–255). Index 0 is reserved for air.
    /// </summary>
    public required byte Index { get; init; }

    /// <summary>
    /// Human-readable material name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// RGBA color components.
    /// </summary>
    public required byte R { get; init; }
    public required byte G { get; init; }
    public required byte B { get; init; }
    public required byte A { get; init; }
}