using VoxelForge.Core;

namespace VoxelForge.App.Console.Commands;

/// <summary>
/// Shared utilities for bake commands that create darkened palette variants.
/// </summary>
internal static class BakeHelper
{
    /// <summary>
    /// Darken an RgbaColor by a factor (1.0 = unchanged, 0.0 = black). Alpha is preserved.
    /// </summary>
    public static RgbaColor Darken(RgbaColor c, float factor)
    {
        return new RgbaColor(
            (byte)(c.R * factor + 0.5f),
            (byte)(c.G * factor + 0.5f),
            (byte)(c.B * factor + 0.5f),
            c.A);
    }

    /// <summary>
    /// Finds an existing palette entry with the exact color, or allocates a new slot.
    /// Returns false if the palette is full.
    /// </summary>
    public static bool FindOrCreateEntry(
        Palette palette,
        RgbaColor color,
        string baseName,
        string suffix,
        Dictionary<RgbaColor, byte> colorCache,
        List<(byte Index, MaterialDef? OldDef, MaterialDef NewDef)> paletteChanges,
        out byte index)
    {
        if (colorCache.TryGetValue(color, out index))
            return true;

        // Check existing palette entries for exact match.
        foreach (var (idx, mat) in palette.Entries)
        {
            if (mat.Color == color)
            {
                colorCache[color] = idx;
                index = idx;
                return true;
            }
        }

        // Allocate new slot — find first index not in palette.
        for (int i = 1; i <= 255; i++)
        {
            byte candidate = (byte)i;
            if (!palette.Contains(candidate))
            {
                var newDef = new MaterialDef
                {
                    Name = $"{baseName}{suffix}",
                    Color = color,
                };
                paletteChanges.Add((candidate, null, newDef));
                palette.Set(candidate, newDef); // Set immediately so next lookup finds it.
                colorCache[color] = candidate;
                index = candidate;
                return true;
            }
        }

        index = 0;
        return false;
    }
}
