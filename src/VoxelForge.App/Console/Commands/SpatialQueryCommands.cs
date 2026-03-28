using VoxelForge.Core;

namespace VoxelForge.App.Console.Commands;

public sealed class GetCubeCommand : IConsoleCommand
{
    public string Name => "getcube";
    public string[] Aliases => ["gc"];
    public string HelpText => "Get voxels in a cube region. Usage: getcube <x1> <y1> <z1> <x2> <y2> <z2>";

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 6)
            return CommandResult.Fail("Usage: getcube <x1> <y1> <z1> <x2> <y2> <z2>");

        if (!TryParseBox(args, out var min, out var max))
            return CommandResult.Fail("Invalid coordinates. Expected integers.");

        var results = new List<string>();
        int count = 0;
        for (int x = min.X; x <= max.X; x++)
        for (int y = min.Y; y <= max.Y; y++)
        for (int z = min.Z; z <= max.Z; z++)
        {
            var val = context.Model.GetVoxel(new Point3(x, y, z));
            if (val.HasValue)
            {
                var name = context.Model.Palette.Get(val.Value)?.Name ?? "?";
                results.Add($"  ({x},{y},{z}) = {val.Value} ({name})");
                count++;
            }
        }

        if (count == 0)
            return CommandResult.Ok($"No voxels in ({min.X},{min.Y},{min.Z}) to ({max.X},{max.Y},{max.Z})");

        results.Insert(0, $"{count} voxels in ({min.X},{min.Y},{min.Z}) to ({max.X},{max.Y},{max.Z}):");
        return CommandResult.Ok(string.Join("\n", results));
    }

    private static bool TryParseBox(string[] args, out Point3 min, out Point3 max)
    {
        min = default;
        max = default;
        if (!int.TryParse(args[0], out int x1) || !int.TryParse(args[1], out int y1) ||
            !int.TryParse(args[2], out int z1) || !int.TryParse(args[3], out int x2) ||
            !int.TryParse(args[4], out int y2) || !int.TryParse(args[5], out int z2))
            return false;
        min = new Point3(Math.Min(x1, x2), Math.Min(y1, y2), Math.Min(z1, z2));
        max = new Point3(Math.Max(x1, x2), Math.Max(y1, y2), Math.Max(z1, z2));
        return true;
    }
}

public sealed class GetSphereCommand : IConsoleCommand
{
    public string Name => "getsphere";
    public string[] Aliases => ["gs"];
    public string HelpText => "Get voxels in a sphere. Usage: getsphere <cx> <cy> <cz> <radius>";

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 4)
            return CommandResult.Fail("Usage: getsphere <cx> <cy> <cz> <radius>");

        if (!int.TryParse(args[0], out int cx) || !int.TryParse(args[1], out int cy) ||
            !int.TryParse(args[2], out int cz) || !float.TryParse(args[3], out float radius))
            return CommandResult.Fail("Invalid arguments.");

        float r2 = radius * radius;
        int ir = (int)MathF.Ceiling(radius);
        var results = new List<string>();
        int count = 0;

        for (int x = cx - ir; x <= cx + ir; x++)
        for (int y = cy - ir; y <= cy + ir; y++)
        for (int z = cz - ir; z <= cz + ir; z++)
        {
            float dx = x - cx, dy = y - cy, dz = z - cz;
            if (dx * dx + dy * dy + dz * dz > r2) continue;

            var val = context.Model.GetVoxel(new Point3(x, y, z));
            if (val.HasValue)
            {
                var name = context.Model.Palette.Get(val.Value)?.Name ?? "?";
                results.Add($"  ({x},{y},{z}) = {val.Value} ({name})");
                count++;
            }
        }

        if (count == 0)
            return CommandResult.Ok($"No voxels within radius {radius} of ({cx},{cy},{cz})");

        results.Insert(0, $"{count} voxels within radius {radius} of ({cx},{cy},{cz}):");
        return CommandResult.Ok(string.Join("\n", results));
    }
}

public sealed class CountCommand : IConsoleCommand
{
    public string Name => "count";
    public string[] Aliases => [];
    public string HelpText => "Count voxels, optionally filtered. Usage: count | count <paletteIndex> | count cube <x1> <y1> <z1> <x2> <y2> <z2>";

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length == 0)
            return CommandResult.Ok($"Total voxels: {context.Model.GetVoxelCount()}");

        if (args[0] == "cube" && args.Length >= 7)
        {
            if (!int.TryParse(args[1], out int x1) || !int.TryParse(args[2], out int y1) ||
                !int.TryParse(args[3], out int z1) || !int.TryParse(args[4], out int x2) ||
                !int.TryParse(args[5], out int y2) || !int.TryParse(args[6], out int z2))
                return CommandResult.Fail("Invalid coordinates.");

            var min = new Point3(Math.Min(x1, x2), Math.Min(y1, y2), Math.Min(z1, z2));
            var max = new Point3(Math.Max(x1, x2), Math.Max(y1, y2), Math.Max(z1, z2));
            int count = 0;
            for (int x = min.X; x <= max.X; x++)
            for (int y = min.Y; y <= max.Y; y++)
            for (int z = min.Z; z <= max.Z; z++)
                if (context.Model.GetVoxel(new Point3(x, y, z)).HasValue) count++;

            return CommandResult.Ok($"Voxels in region: {count}");
        }

        if (byte.TryParse(args[0], out byte idx))
        {
            int count = context.Model.Voxels.Values.Count(v => v == idx);
            var name = context.Model.Palette.Get(idx)?.Name ?? "?";
            return CommandResult.Ok($"Voxels with palette {idx} ({name}): {count}");
        }

        return CommandResult.Fail("Usage: count | count <paletteIndex> | count cube <x1> <y1> <z1> <x2> <y2> <z2>");
    }
}
