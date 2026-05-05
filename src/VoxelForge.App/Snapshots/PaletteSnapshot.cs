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
    /// Total number of palette slots including unused indices.
    /// This may be larger than <see cref="Entries"/>.Count if there are gaps.
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