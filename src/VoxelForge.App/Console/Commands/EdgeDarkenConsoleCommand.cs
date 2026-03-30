using VoxelForge.Core;

namespace VoxelForge.App.Console.Commands;

/// <summary>
/// Darkens voxels at the boundary/silhouette of the model by detecting voxels
/// with fewer face-adjacent neighbors. Creates discrete palette variants.
/// Optionally tints edges toward a specified color.
/// </summary>
public sealed class EdgeDarkenConsoleCommand : IConsoleCommand
{
    public string Name => "edge-darken";
    public string[] Aliases => ["edged"];
    public string HelpText => "Darken boundary voxels. Usage: edge-darken [strength] [steps] [tint]\n" +
        "  strength: 0-1, how much to darken edges (default 0.3)\n" +
        "  steps: discrete levels (default 3)\n" +
        "  tint: optional color to blend toward (e.g. #2B1B4E)";

    private static readonly Point3[] FaceNeighbors =
    [
        new(1, 0, 0), new(-1, 0, 0),
        new(0, 1, 0), new(0, -1, 0),
        new(0, 0, 1), new(0, 0, -1),
    ];

    public CommandResult Execute(string[] args, CommandContext context)
    {
        float strength = 0.3f;
        int steps = 3;
        RgbaColor? tint = null;

        if (args.Length >= 1 && !float.TryParse(args[0], out strength))
            return CommandResult.Fail($"Invalid strength: {args[0]}");
        if (args.Length >= 2 && !int.TryParse(args[1], out steps))
            return CommandResult.Fail($"Invalid steps: {args[1]}");
        if (args.Length >= 3)
        {
            if (!RgbaColor.TryParse(args[2], out var parsed))
                return CommandResult.Fail($"Invalid tint color: {args[2]}");
            tint = parsed;
        }

        if (strength <= 0f || strength > 1f)
            return CommandResult.Fail("Strength must be between 0 (exclusive) and 1.");
        if (steps < 2 || steps > 8)
            return CommandResult.Fail("Steps must be between 2 and 8.");

        var model = context.Model;
        var voxels = model.Voxels;

        if (voxels.Count == 0)
            return CommandResult.Fail("No voxels in model.");

        // Compute "edge-ness" per voxel: fewer face neighbors = more edge-like.
        // A voxel with 0 face neighbors is maximally exposed (isolated).
        // A voxel with 5 face neighbors is barely an edge.
        // A voxel with 6 face neighbors is interior (no exposed faces).
        var edgeValues = new Dictionary<Point3, float>(voxels.Count);
        foreach (var pos in voxels.Keys)
        {
            int faceCount = 0;
            foreach (var offset in FaceNeighbors)
            {
                var neighbor = new Point3(pos.X + offset.X, pos.Y + offset.Y, pos.Z + offset.Z);
                if (voxels.ContainsKey(neighbor))
                    faceCount++;
            }
            // Invert: 0 neighbors = 1.0 (max edge), 5 neighbors = 0.0 (barely edge).
            // Skip fully interior voxels (6 neighbors).
            if (faceCount >= 6) continue;
            float edgeness = 1f - faceCount / 5f;
            if (edgeness > 0f)
                edgeValues[pos] = edgeness;
        }

        // Quantize and apply.
        var paletteChanges = new List<(byte Index, MaterialDef? OldDef, MaterialDef NewDef)>();
        var voxelChanges = new List<(Point3 Pos, byte OldIndex, byte NewIndex)>();
        var colorCache = new Dictionary<RgbaColor, byte>();
        bool paletteFull = false;

        foreach (var (pos, edgeness) in edgeValues)
        {
            byte paletteIdx = voxels[pos];
            int step = (int)(edgeness * (steps - 1) + 0.5f);
            if (step == 0) continue;

            float quantized = step / (float)(steps - 1);
            float t = quantized * strength;

            var baseDef = model.Palette.Get(paletteIdx);
            if (baseDef == null) continue;

            RgbaColor result;
            if (tint.HasValue)
                result = Lerp(baseDef.Color, tint.Value, t);
            else
                result = BakeHelper.Darken(baseDef.Color, 1f - t);

            if (result == baseDef.Color) continue;

            if (!BakeHelper.FindOrCreateEntry(
                model.Palette, result, baseDef.Name, $"_edge{step}",
                colorCache, paletteChanges, out byte newIdx))
            {
                paletteFull = true;
                break;
            }

            voxelChanges.Add((pos, paletteIdx, newIdx));
        }

        if (voxelChanges.Count == 0 && !paletteFull)
            return CommandResult.Ok("No boundary voxels needed darkening.");

        var cmd = new App.Commands.VoxelBakeCommand(
            model, $"Edge darken (strength={strength}, steps={steps})",
            paletteChanges, voxelChanges);
        context.UndoStack.Execute(cmd);
        context.OnModelChanged?.Invoke();

        string msg = $"Edge darkened: {voxelChanges.Count} voxels, {paletteChanges.Count} palette entries created.";
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
