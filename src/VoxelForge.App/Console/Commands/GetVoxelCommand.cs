using VoxelForge.Core;
using VoxelForge.Core.Services;

namespace VoxelForge.App.Console.Commands;

public sealed class GetVoxelCommand : IConsoleCommand
{
    private readonly VoxelQueryService _queryService;

    public string Name => "get";
    public string[] Aliases => ["g"];
    public string HelpText => "Get voxel at position. Usage: get <x> <y> <z>";

    public GetVoxelCommand(VoxelQueryService queryService)
    {
        _queryService = queryService;
    }

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 3)
            return CommandResult.Fail("Usage: get <x> <y> <z>");

        if (!int.TryParse(args[0], out int x) || !int.TryParse(args[1], out int y) ||
            !int.TryParse(args[2], out int z))
            return CommandResult.Fail("Invalid arguments. Expected integers for x,y,z.");

        var result = _queryService.GetVoxel(context.Model, new Point3(x, y, z));
        if (result.PaletteIndex is null)
            return CommandResult.Ok($"({x},{y},{z}) = air");

        return CommandResult.Ok($"({x},{y},{z}) = {result.PaletteIndex.Value} ({result.MaterialName})");
    }
}
