using VoxelForge.Core;

namespace VoxelForge.App.Console.Commands;

public sealed class GetVoxelCommand : IConsoleCommand
{
    public string Name => "get";
    public string[] Aliases => ["g"];
    public string HelpText => "Get voxel at position. Usage: get <x> <y> <z>";

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 3)
            return CommandResult.Fail("Usage: get <x> <y> <z>");

        if (!int.TryParse(args[0], out int x) || !int.TryParse(args[1], out int y) ||
            !int.TryParse(args[2], out int z))
            return CommandResult.Fail("Invalid arguments. Expected integers for x,y,z.");

        var pos = new Point3(x, y, z);
        var value = context.Model.GetVoxel(pos);

        if (value is null)
            return CommandResult.Ok($"({x},{y},{z}) = air");

        var mat = context.Model.Palette.Get(value.Value);
        var name = mat?.Name ?? "unknown";
        return CommandResult.Ok($"({x},{y},{z}) = {value.Value} ({name})");
    }
}
