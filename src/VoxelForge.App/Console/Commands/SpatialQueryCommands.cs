using VoxelForge.Core;
using VoxelForge.Core.Services;

namespace VoxelForge.App.Console.Commands;

public sealed class GetCubeCommand : IConsoleCommand
{
    private readonly VoxelQueryService _queryService;

    public string Name => "getcube";
    public string[] Aliases => ["gc"];
    public string HelpText => "Get voxels in a cube region. Usage: getcube <x1> <y1> <z1> <x2> <y2> <z2>";

    public GetCubeCommand(VoxelQueryService queryService)
    {
        _queryService = queryService;
    }

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 6)
            return CommandResult.Fail("Usage: getcube <x1> <y1> <z1> <x2> <y2> <z2>");

        if (!TryParseBox(args, out var min, out var max))
            return CommandResult.Fail("Invalid coordinates. Expected integers.");

        var queryResult = _queryService.QueryBox(context.Model, new VoxelBoxQueryRequest(min, max));
        if (queryResult.Count == 0)
            return CommandResult.Ok($"No voxels in ({min.X},{min.Y},{min.Z}) to ({max.X},{max.Y},{max.Z})");

        var lines = new List<string>
        {
            $"{queryResult.Count} voxels in ({min.X},{min.Y},{min.Z}) to ({max.X},{max.Y},{max.Z}):",
        };
        for (int i = 0; i < queryResult.Voxels.Count; i++)
        {
            var voxel = queryResult.Voxels[i];
            lines.Add($"  ({voxel.Position.X},{voxel.Position.Y},{voxel.Position.Z}) = {voxel.PaletteIndex} ({voxel.MaterialName})");
        }

        return CommandResult.Ok(string.Join("\n", lines));
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
    private readonly VoxelQueryService _queryService;

    public string Name => "getsphere";
    public string[] Aliases => ["gs"];
    public string HelpText => "Get voxels in a sphere. Usage: getsphere <cx> <cy> <cz> <radius>";

    public GetSphereCommand(VoxelQueryService queryService)
    {
        _queryService = queryService;
    }

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 4)
            return CommandResult.Fail("Usage: getsphere <cx> <cy> <cz> <radius>");

        if (!int.TryParse(args[0], out int cx) || !int.TryParse(args[1], out int cy) ||
            !int.TryParse(args[2], out int cz) || !float.TryParse(args[3], out float radius))
            return CommandResult.Fail("Invalid arguments.");

        var center = new Point3(cx, cy, cz);
        var queryResult = _queryService.QuerySphere(context.Model, new VoxelSphereQueryRequest(center, radius));
        if (queryResult.Count == 0)
            return CommandResult.Ok($"No voxels within radius {radius} of ({cx},{cy},{cz})");

        var lines = new List<string>
        {
            $"{queryResult.Count} voxels within radius {radius} of ({cx},{cy},{cz}):",
        };
        for (int i = 0; i < queryResult.Voxels.Count; i++)
        {
            var voxel = queryResult.Voxels[i];
            lines.Add($"  ({voxel.Position.X},{voxel.Position.Y},{voxel.Position.Z}) = {voxel.PaletteIndex} ({voxel.MaterialName})");
        }

        return CommandResult.Ok(string.Join("\n", lines));
    }
}

public sealed class CountCommand : IConsoleCommand
{
    private readonly VoxelQueryService _queryService;

    public string Name => "count";
    public string[] Aliases => [];
    public string HelpText => "Count voxels, optionally filtered. Usage: count | count <paletteIndex> | count cube <x1> <y1> <z1> <x2> <y2> <z2>";

    public CountCommand(VoxelQueryService queryService)
    {
        _queryService = queryService;
    }

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length == 0)
            return CommandResult.Ok($"Total voxels: {_queryService.CountVoxels(context.Model)}");

        if (args[0] == "cube" && args.Length >= 7)
        {
            if (!int.TryParse(args[1], out int x1) || !int.TryParse(args[2], out int y1) ||
                !int.TryParse(args[3], out int z1) || !int.TryParse(args[4], out int x2) ||
                !int.TryParse(args[5], out int y2) || !int.TryParse(args[6], out int z2))
                return CommandResult.Fail("Invalid coordinates.");

            var min = new Point3(Math.Min(x1, x2), Math.Min(y1, y2), Math.Min(z1, z2));
            var max = new Point3(Math.Max(x1, x2), Math.Max(y1, y2), Math.Max(z1, z2));
            int count = _queryService.CountVoxelsInBox(context.Model, new VoxelBoxQueryRequest(min, max));

            return CommandResult.Ok($"Voxels in region: {count}");
        }

        if (byte.TryParse(args[0], out byte idx))
        {
            int count = _queryService.CountVoxelsByPalette(context.Model, idx);
            var name = context.Model.Palette.Get(idx)?.Name ?? "?";
            return CommandResult.Ok($"Voxels with palette {idx} ({name}): {count}");
        }

        return CommandResult.Fail("Usage: count | count <paletteIndex> | count cube <x1> <y1> <z1> <x2> <y2> <z2>");
    }
}
