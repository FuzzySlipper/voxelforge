using VoxelForge.Core;

namespace VoxelForge.App.Console.Commands;

public sealed class RemoveVoxelConsoleCommand : IConsoleCommand
{
    public string Name => "remove";
    public string[] Aliases => ["rm", "delete"];
    public string HelpText => "Remove a voxel. Usage: remove <x> <y> <z>";

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 3)
            return CommandResult.Fail("Usage: remove <x> <y> <z>");

        if (!int.TryParse(args[0], out int x) || !int.TryParse(args[1], out int y) ||
            !int.TryParse(args[2], out int z))
            return CommandResult.Fail("Invalid arguments. Expected integers for x,y,z.");

        var pos = new Point3(x, y, z);
        var cmd = new App.Commands.RemoveVoxelCommand(context.Model, pos);
        context.UndoStack.Execute(cmd);
        context.OnModelChanged?.Invoke();

        return CommandResult.Ok($"Removed ({x},{y},{z})");
    }
}
