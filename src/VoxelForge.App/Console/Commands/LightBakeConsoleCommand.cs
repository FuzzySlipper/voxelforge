using System.Globalization;
using VoxelForge.Core;

namespace VoxelForge.App.Console.Commands;

/// <summary>
/// Bakes directional lighting into voxel colors using face normals.
/// Stackable — multiple passes can be applied. Creates discrete palette variants.
/// </summary>
public sealed class LightBakeConsoleCommand : IConsoleCommand
{
    public string Name => "light-bake";
    public string[] Aliases => ["lbake"];
    public string HelpText =>
        "Bake directional light. Usage: light-bake <dx,dy,dz> [intensity] [steps] [tint]\n" +
        "  direction: light direction as x,y,z (e.g. 1,0.8,0.5)\n" +
        "  intensity: 0-1, darkening on shadow side (default 0.4)\n" +
        "  steps: discrete levels (default 4)\n" +
        "  tint: optional warm/cool color for lit side (e.g. #FFF4E0)";

    private static readonly Point3[] FaceNeighbors =
    [
        new(1, 0, 0), new(-1, 0, 0),
        new(0, 1, 0), new(0, -1, 0),
        new(0, 0, 1), new(0, 0, -1),
    ];

    // Unit normals corresponding to each face direction.
    private static readonly (float X, float Y, float Z)[] FaceNormals =
    [
        (1, 0, 0), (-1, 0, 0),
        (0, 1, 0), (0, -1, 0),
        (0, 0, 1), (0, 0, -1),
    ];

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 1)
            return CommandResult.Fail(HelpText);

        // Parse direction vector.
        var dirParts = args[0].Split(',');
        if (dirParts.Length != 3 ||
            !float.TryParse(dirParts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float dx) ||
            !float.TryParse(dirParts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float dy) ||
            !float.TryParse(dirParts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float dz))
            return CommandResult.Fail($"Invalid direction: {args[0]}. Expected x,y,z (e.g. 1,0.8,0.5)");

        float len = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        if (len < 1e-6f)
            return CommandResult.Fail("Direction vector must be non-zero.");
        dx /= len; dy /= len; dz /= len;

        float intensity = 0.4f;
        int steps = 4;
        RgbaColor? tint = null;

        if (args.Length >= 2 && !float.TryParse(args[1], out intensity))
            return CommandResult.Fail($"Invalid intensity: {args[1]}");
        if (args.Length >= 3 && !int.TryParse(args[2], out steps))
            return CommandResult.Fail($"Invalid steps: {args[2]}");
        if (args.Length >= 4)
        {
            if (!RgbaColor.TryParse(args[3], out var parsed))
                return CommandResult.Fail($"Invalid tint color: {args[3]}");
            tint = parsed;
        }

        if (intensity <= 0f || intensity > 1f)
            return CommandResult.Fail("Intensity must be between 0 (exclusive) and 1.");
        if (steps < 2 || steps > 16)
            return CommandResult.Fail("Steps must be between 2 and 16.");

        var model = context.Model;
        var voxels = model.Voxels;

        if (voxels.Count == 0)
            return CommandResult.Fail("No voxels in model.");

        // For each voxel, compute a lighting factor based on its exposed face normals
        // dotted with the light direction. Exposed faces facing the light brighten;
        // faces facing away darken.
        var lightValues = new Dictionary<Point3, float>(voxels.Count);
        foreach (var pos in voxels.Keys)
        {
            float totalDot = 0f;
            int exposedCount = 0;

            for (int i = 0; i < 6; i++)
            {
                var offset = FaceNeighbors[i];
                var neighbor = new Point3(pos.X + offset.X, pos.Y + offset.Y, pos.Z + offset.Z);
                if (voxels.ContainsKey(neighbor)) continue; // Face not exposed.

                var n = FaceNormals[i];
                float dot = n.X * dx + n.Y * dy + n.Z * dz;
                totalDot += dot;
                exposedCount++;
            }

            if (exposedCount == 0) continue; // Fully interior.

            // Average dot product across exposed faces, range [-1, 1].
            // Map to [0, 1] where 0 = fully in shadow, 1 = fully lit.
            float avgDot = totalDot / exposedCount;
            float lightFactor = (avgDot + 1f) / 2f; // Remap -1..1 to 0..1.
            lightValues[pos] = lightFactor;
        }

        // Quantize and apply. Shadow side gets darkened, lit side optionally tinted.
        var paletteChanges = new List<(byte Index, MaterialDef? OldDef, MaterialDef NewDef)>();
        var voxelChanges = new List<(Point3 Pos, byte OldIndex, byte NewIndex)>();
        var colorCache = new Dictionary<RgbaColor, byte>();
        bool paletteFull = false;

        foreach (var (pos, lightFactor) in lightValues)
        {
            byte paletteIdx = voxels[pos];

            // Quantize light factor.
            int step = (int)(lightFactor * (steps - 1) + 0.5f);
            float quantized = step / (float)(steps - 1);

            // Neutral is 0.5 (no change). Below = darken, above = lighten/tint.
            // Map to a darken factor: 0 -> (1 - intensity), 0.5 -> 1.0, 1.0 -> 1.0 (or tint).
            // Shadow side: factor = 1 - (1 - quantized * 2) * intensity for quantized < 0.5.
            // Lit side: factor = 1.0 (or apply tint blend).

            var baseDef = model.Palette.Get(paletteIdx);
            if (baseDef == null) continue;

            RgbaColor result;
            if (quantized < 0.5f)
            {
                // Shadow: darken proportionally.
                float shadowStrength = (1f - quantized * 2f) * intensity;
                result = BakeHelper.Darken(baseDef.Color, 1f - shadowStrength);
            }
            else if (quantized > 0.5f && tint.HasValue)
            {
                // Lit side with tint: blend toward tint color.
                float tintStrength = (quantized * 2f - 1f) * intensity * 0.5f;
                result = Lerp(baseDef.Color, tint.Value, tintStrength);
            }
            else
            {
                continue; // Neutral — no change.
            }

            if (result == baseDef.Color) continue;

            if (!BakeHelper.FindOrCreateEntry(
                model.Palette, result, baseDef.Name, $"_lit{step}",
                colorCache, paletteChanges, out byte newIdx))
            {
                paletteFull = true;
                break;
            }

            voxelChanges.Add((pos, paletteIdx, newIdx));
        }

        if (voxelChanges.Count == 0 && !paletteFull)
            return CommandResult.Ok("No voxels affected by light bake.");

        var cmd = new App.Commands.VoxelBakeCommand(
            model, $"Light bake dir=({dx:F1},{dy:F1},{dz:F1}) intensity={intensity}",
            paletteChanges, voxelChanges);
        context.UndoStack.Execute(cmd);
        context.OnModelChanged?.Invoke();

        string msg = $"Light baked: {voxelChanges.Count} voxels, {paletteChanges.Count} palette entries created.";
        if (paletteFull)
            msg += " WARNING: Palette full — some voxels were skipped.";
        return CommandResult.Ok(msg);
    }

    private static RgbaColor Lerp(RgbaColor a, RgbaColor b, float t)
    {
        return new RgbaColor(
            (byte)(a.R + (b.R - a.R) * t + 0.5f),
            (byte)(a.G + (b.G - a.G) * t + 0.5f),
            (byte)(a.B + (b.B - a.B) * t + 0.5f),
            a.A);
    }
}
