using VoxelForge.Core;

namespace VoxelForge.App.Console.Commands;

/// <summary>
/// Bakes ambient occlusion into voxel colors by counting occupied neighbors
/// and darkening voxels in concavities. Creates discrete palette variants.
/// </summary>
public sealed class AoBakeConsoleCommand : IConsoleCommand
{
    public string Name => "ao-bake";
    public string[] Aliases => ["ao"];
    public string HelpText => "Bake ambient occlusion. Usage: ao-bake [intensity] [steps]\n" +
        "  intensity: 0-1, how much to darken (default 0.5)\n" +
        "  steps: number of discrete AO levels (default 4)";

    // 26 neighbor offsets (all combinations of -1,0,+1 except 0,0,0).
    private static readonly Point3[] Neighbors = BuildNeighborOffsets();

    public CommandResult Execute(string[] args, CommandContext context)
    {
        float intensity = 0.5f;
        int steps = 4;

        if (args.Length >= 1 && !float.TryParse(args[0], out intensity))
            return CommandResult.Fail($"Invalid intensity: {args[0]}");
        if (args.Length >= 2 && !int.TryParse(args[1], out steps))
            return CommandResult.Fail($"Invalid steps: {args[1]}");

        if (intensity <= 0f || intensity > 1f)
            return CommandResult.Fail("Intensity must be between 0 (exclusive) and 1.");
        if (steps < 2 || steps > 16)
            return CommandResult.Fail("Steps must be between 2 and 16.");

        var model = context.Model;
        var voxels = model.Voxels;

        if (voxels.Count == 0)
            return CommandResult.Fail("No voxels in model.");

        // Compute raw AO per voxel (0 = open, 1 = fully enclosed).
        var aoValues = new Dictionary<Point3, float>(voxels.Count);
        foreach (var pos in voxels.Keys)
        {
            int occupied = 0;
            foreach (var offset in Neighbors)
            {
                var neighbor = new Point3(pos.X + offset.X, pos.Y + offset.Y, pos.Z + offset.Z);
                if (voxels.ContainsKey(neighbor))
                    occupied++;
            }
            aoValues[pos] = occupied / 26f;
        }

        // Quantize and apply.
        var paletteChanges = new List<(byte Index, MaterialDef? OldDef, MaterialDef NewDef)>();
        var voxelChanges = new List<(Point3 Pos, byte OldIndex, byte NewIndex)>();
        var colorCache = new Dictionary<RgbaColor, byte>();
        bool paletteFull = false;

        foreach (var (pos, paletteIdx) in voxels)
        {
            float rawAo = aoValues[pos];
            // Quantize to discrete step.
            int step = (int)(rawAo * (steps - 1) + 0.5f);
            if (step == 0) continue; // No darkening needed.

            float quantizedAo = step / (float)(steps - 1);
            float factor = 1f - quantizedAo * intensity;

            var baseDef = model.Palette.Get(paletteIdx);
            if (baseDef == null) continue;

            var darkened = BakeHelper.Darken(baseDef.Color, factor);
            if (darkened == baseDef.Color) continue; // Rounding produced same color.

            if (!BakeHelper.FindOrCreateEntry(
                model.Palette, darkened, baseDef.Name, $"_ao{step}",
                colorCache, paletteChanges, out byte newIdx))
            {
                paletteFull = true;
                break;
            }

            voxelChanges.Add((pos, paletteIdx, newIdx));
        }

        if (voxelChanges.Count == 0 && !paletteFull)
            return CommandResult.Ok("No voxels needed AO darkening.");

        var cmd = new App.Commands.VoxelBakeCommand(
            model, $"AO bake (intensity={intensity}, steps={steps})",
            paletteChanges, voxelChanges);
        context.UndoStack.Execute(cmd);
        context.OnModelChanged?.Invoke();

        string result = $"AO baked: {voxelChanges.Count} voxels darkened, {paletteChanges.Count} palette entries created.";
        if (paletteFull)
            result += " WARNING: Palette full — some voxels were skipped.";
        return CommandResult.Ok(result);
    }

    private static Point3[] BuildNeighborOffsets()
    {
        var offsets = new List<Point3>();
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dz = -1; dz <= 1; dz++)
        {
            if (dx == 0 && dy == 0 && dz == 0) continue;
            offsets.Add(new Point3(dx, dy, dz));
        }
        return offsets.ToArray();
    }
}
