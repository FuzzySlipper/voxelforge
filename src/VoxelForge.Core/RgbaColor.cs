namespace VoxelForge.Core;

/// <summary>
/// Immutable RGBA color stored as four bytes.
/// </summary>
public readonly record struct RgbaColor(byte R, byte G, byte B, byte A = 255)
{
    public static readonly RgbaColor White = new(255, 255, 255);
    public static readonly RgbaColor Magenta = new(255, 0, 255);
}
