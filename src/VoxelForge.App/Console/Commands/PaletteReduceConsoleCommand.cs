using VoxelForge.Core;

namespace VoxelForge.App.Console.Commands;

/// <summary>
/// Reduces the palette to a target number of colors by iteratively merging
/// the most perceptually similar colors in OKLAB space. Consolidates voxel
/// indices onto surviving entries and removes freed palette slots.
/// </summary>
public sealed class PaletteReduceConsoleCommand : IConsoleCommand
{
    public string Name => "palette-reduce";
    public string[] Aliases => ["preduce"];
    public string HelpText =>
        "Reduce palette to N colors. Usage: palette-reduce <count> [--preserve #RRGGBB,...]\n" +
        "  Merges similar colors in OKLAB space. Pinned colors are never merged away.";

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 1 || !int.TryParse(args[0], out int targetCount) || targetCount < 1)
            return CommandResult.Fail("Usage: palette-reduce <count> [--preserve #RRGGBB,...]");

        // Parse optional --preserve flag.
        var pinned = new HashSet<RgbaColor>();
        for (int i = 1; i < args.Length - 1; i++)
        {
            if (args[i] == "--preserve")
            {
                foreach (var token in args[i + 1].Split(','))
                {
                    if (RgbaColor.TryParse(token.Trim(), out var pc))
                        pinned.Add(pc);
                    else
                        return CommandResult.Fail($"Cannot parse pinned color: {token}");
                }
            }
        }

        var model = context.Model;
        var palette = model.Palette;
        if (palette.Count == 0)
            return CommandResult.Fail("Palette is empty.");

        // Find which palette indices are actually used by voxels.
        var usedIndices = new HashSet<byte>();
        foreach (var idx in model.Voxels.Values)
            usedIndices.Add(idx);

        // Gather unique colors and which palette indices map to each.
        var colorToIndices = new Dictionary<RgbaColor, List<byte>>();
        foreach (var (idx, mat) in palette.Entries)
        {
            if (!colorToIndices.TryGetValue(mat.Color, out var list))
            {
                list = [];
                colorToIndices[mat.Color] = list;
            }
            list.Add(idx);
        }

        int uniqueCount = colorToIndices.Count;
        if (targetCount >= uniqueCount)
            return CommandResult.Ok($"Palette has {uniqueCount} unique colors (<= {targetCount}). Nothing to do.");

        // Build cluster list.
        var clusters = new List<Cluster>();
        foreach (var (color, indices) in colorToIndices)
        {
            clusters.Add(new Cluster
            {
                Representative = color,
                IsPinned = pinned.Contains(color),
                Members = [(color, indices)],
            });
        }

        // Agglomerative merge: repeatedly merge the two closest clusters.
        while (clusters.Count > targetCount)
        {
            float bestDist = float.MaxValue;
            int bestA = -1, bestB = -1;

            for (int i = 0; i < clusters.Count; i++)
            {
                for (int j = i + 1; j < clusters.Count; j++)
                {
                    if (clusters[i].IsPinned && clusters[j].IsPinned)
                        continue;

                    float d = RgbaColor.OklabDistance(clusters[i].Representative, clusters[j].Representative);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        bestA = i;
                        bestB = j;
                    }
                }
            }

            if (bestA < 0) break;

            var cA = clusters[bestA];
            var cB = clusters[bestB];

            RgbaColor rep;
            if (cA.IsPinned)
                rep = cA.Representative;
            else if (cB.IsPinned)
                rep = cB.Representative;
            else
                rep = cA.Members.Sum(m => m.Indices.Count) >= cB.Members.Sum(m => m.Indices.Count)
                    ? cA.Representative
                    : cB.Representative;

            clusters[bestA] = new Cluster
            {
                Representative = rep,
                IsPinned = cA.IsPinned || cB.IsPinned,
                Members = [.. cA.Members, .. cB.Members],
            };
            clusters.RemoveAt(bestB);
        }

        // For each cluster, pick one surviving palette index and consolidate.
        var paletteChanges = new List<(byte Index, MaterialDef OldDef, MaterialDef? NewDef)>();
        var voxelChanges = new List<(Point3 Pos, byte OldIndex, byte NewIndex)>();

        // Build a map from old index -> new index for voxel reassignment.
        var indexRemap = new Dictionary<byte, byte>();

        foreach (var cluster in clusters)
        {
            // Collect all palette indices in this cluster.
            var allIndices = cluster.Members.SelectMany(m => m.Indices).ToList();

            // Pick surviving index: prefer one that already has the representative color,
            // then prefer one that's actually used by voxels.
            byte survivorIdx = allIndices
                .OrderByDescending(i => palette.Get(i)!.Color == cluster.Representative ? 1 : 0)
                .ThenByDescending(i => usedIndices.Contains(i) ? 1 : 0)
                .First();

            // If survivor's color differs from representative, record the color change.
            var survivorDef = palette.Get(survivorIdx)!;
            if (survivorDef.Color != cluster.Representative)
            {
                var newDef = new MaterialDef
                {
                    Name = survivorDef.Name,
                    Color = cluster.Representative,
                    Metadata = survivorDef.Metadata,
                };
                paletteChanges.Add((survivorIdx, survivorDef, newDef));
            }

            // Map all other indices to the survivor and mark them for removal.
            foreach (byte idx in allIndices)
            {
                if (idx == survivorIdx) continue;
                indexRemap[idx] = survivorIdx;
                paletteChanges.Add((idx, palette.Get(idx)!, null)); // null = remove
            }
        }

        // Reassign voxels whose palette index was merged away.
        foreach (var (pos, oldIdx) in model.Voxels)
        {
            if (indexRemap.TryGetValue(oldIdx, out byte newIdx))
                voxelChanges.Add((pos, oldIdx, newIdx));
        }

        if (paletteChanges.Count == 0 && voxelChanges.Count == 0)
            return CommandResult.Ok("No changes needed.");

        int removedEntries = paletteChanges.Count(c => c.NewDef == null);
        var cmd = new App.Commands.PaletteReduceCommand(
            model,
            $"Palette reduce ({uniqueCount} -> {clusters.Count} colors)",
            paletteChanges, voxelChanges);
        context.UndoStack.Execute(cmd);
        context.OnModelChanged?.Invoke();

        return CommandResult.Ok(
            $"Reduced from {uniqueCount} to {clusters.Count} unique colors. " +
            $"Reassigned {voxelChanges.Count} voxel(s), removed {removedEntries} palette entry(s).");
    }

    private sealed class Cluster
    {
        public required RgbaColor Representative { get; set; }
        public required bool IsPinned { get; set; }
        public required List<(RgbaColor Color, List<byte> Indices)> Members { get; set; }
    }
}
