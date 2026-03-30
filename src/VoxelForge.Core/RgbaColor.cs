using System;

namespace VoxelForge.Core;

/// <summary>
/// Immutable RGBA color stored as four bytes.
/// </summary>
public readonly record struct RgbaColor(byte R, byte G, byte B, byte A = 255)
{
    public static readonly RgbaColor White = new(255, 255, 255);
    public static readonly RgbaColor Magenta = new(255, 0, 255);

    /// <summary>
    /// Perceptual distance between two colors in OKLAB space.
    /// Returns a value where 0 = identical, ~1 = maximum difference.
    /// </summary>
    public static float OklabDistance(RgbaColor a, RgbaColor b)
    {
        ToOklab(a, out float L1, out float a1, out float b1);
        ToOklab(b, out float L2, out float a2, out float b2);
        float dL = L1 - L2, da = a1 - a2, db = b1 - b2;
        return MathF.Sqrt(dL * dL + da * da + db * db);
    }

    /// <summary>
    /// Try to parse a color from "R,G,B", "R,G,B,A", or "#RRGGBB" / "#RRGGBBAA" format.
    /// </summary>
    public static bool TryParse(string text, out RgbaColor color)
    {
        color = default;

        if (text.StartsWith('#'))
        {
            var hex = text.AsSpan(1);
            if (hex.Length == 6 &&
                byte.TryParse(hex[..2], System.Globalization.NumberStyles.HexNumber, null, out byte hr) &&
                byte.TryParse(hex[2..4], System.Globalization.NumberStyles.HexNumber, null, out byte hg) &&
                byte.TryParse(hex[4..6], System.Globalization.NumberStyles.HexNumber, null, out byte hb))
            {
                color = new RgbaColor(hr, hg, hb);
                return true;
            }
            if (hex.Length == 8 &&
                byte.TryParse(hex[..2], System.Globalization.NumberStyles.HexNumber, null, out hr) &&
                byte.TryParse(hex[2..4], System.Globalization.NumberStyles.HexNumber, null, out hg) &&
                byte.TryParse(hex[4..6], System.Globalization.NumberStyles.HexNumber, null, out hb) &&
                byte.TryParse(hex[6..8], System.Globalization.NumberStyles.HexNumber, null, out byte ha))
            {
                color = new RgbaColor(hr, hg, hb, ha);
                return true;
            }
            return false;
        }

        var parts = text.Split(',');
        if (parts.Length is 3 or 4 &&
            byte.TryParse(parts[0], out byte r) &&
            byte.TryParse(parts[1], out byte g) &&
            byte.TryParse(parts[2], out byte b2))
        {
            byte a2 = 255;
            if (parts.Length == 4 && !byte.TryParse(parts[3], out a2))
                return false;
            color = new RgbaColor(r, g, b2, parts.Length == 4 ? a2 : (byte)255);
            return true;
        }

        return false;
    }

    private static float Linearize(byte c)
    {
        float s = c / 255f;
        return s <= 0.04045f ? s / 12.92f : MathF.Pow((s + 0.055f) / 1.055f, 2.4f);
    }

    private static void ToOklab(RgbaColor c, out float L, out float a, out float b)
    {
        float r = Linearize(c.R), g = Linearize(c.G), bl = Linearize(c.B);

        float l = 0.4122214708f * r + 0.5363325363f * g + 0.0514459929f * bl;
        float m = 0.2119034982f * r + 0.6806995451f * g + 0.1073969566f * bl;
        float s = 0.0883024619f * r + 0.2817188376f * g + 0.6299787005f * bl;

        float lc = MathF.Cbrt(l);
        float mc = MathF.Cbrt(m);
        float sc = MathF.Cbrt(s);

        L = 0.2104542553f * lc + 0.7936177850f * mc - 0.0040720468f * sc;
        a = 1.9779984951f * lc - 2.4285922050f * mc + 0.4505937099f * sc;
        b = 0.0259040371f * lc + 0.7827717662f * mc - 0.8086757660f * sc;
    }
}
