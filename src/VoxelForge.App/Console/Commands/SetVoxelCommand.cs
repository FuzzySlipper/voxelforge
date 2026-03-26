using VoxelForge.Core;

namespace VoxelForge.App.Console.Commands;

public sealed class SetVoxelConsoleCommand : IConsoleCommand
{
    public string Name => "set";
    public string[] Aliases => ["s", "place"];
    public string HelpText => "Set a voxel. Usage: set <x> <y> <z> <paletteIndex>";

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 4)
            return CommandResult.Fail("Usage: set <x> <y> <z> <paletteIndex>");

        if (!int.TryParse(args[0], out int x) || !int.TryParse(args[1], out int y) ||
            !int.TryParse(args[2], out int z) || !byte.TryParse(args[3], out byte idx))
            return CommandResult.Fail("Invalid arguments. Expected integers for x,y,z and byte for index.");

        var pos = new Point3(x, y, z);
        var cmd = new App.Commands.SetVoxelCommand(context.Model, pos, idx);
        context.UndoStack.Execute(cmd);
        context.OnModelChanged?.Invoke();

        return CommandResult.Ok($"Set ({x},{y},{z}) = {idx}");
    }
}
