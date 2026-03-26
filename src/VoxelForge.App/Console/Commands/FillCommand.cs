using VoxelForge.Core;

namespace VoxelForge.App.Console.Commands;

public sealed class FillCommand : IConsoleCommand
{
    public string Name => "fill";
    public string[] Aliases => ["f"];
    public string HelpText => "Fill a region. Usage: fill <x1> <y1> <z1> <x2> <y2> <z2> <paletteIndex>";

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 7)
            return CommandResult.Fail("Usage: fill <x1> <y1> <z1> <x2> <y2> <z2> <paletteIndex>");

        if (!int.TryParse(args[0], out int x1) || !int.TryParse(args[1], out int y1) ||
            !int.TryParse(args[2], out int z1) || !int.TryParse(args[3], out int x2) ||
            !int.TryParse(args[4], out int y2) || !int.TryParse(args[5], out int z2) ||
            !byte.TryParse(args[6], out byte idx))
            return CommandResult.Fail("Invalid arguments.");

        var min = new Point3(Math.Min(x1, x2), Math.Min(y1, y2), Math.Min(z1, z2));
        var max = new Point3(Math.Max(x1, x2), Math.Max(y1, y2), Math.Max(z1, z2));
        var cmd = new App.Commands.FillRegionCommand(context.Model, min, max, idx);
        context.UndoStack.Execute(cmd);
        context.OnModelChanged?.Invoke();

        int count = (max.X - min.X + 1) * (max.Y - min.Y + 1) * (max.Z - min.Z + 1);
        return CommandResult.Ok($"Filled {count} voxels from ({min.X},{min.Y},{min.Z}) to ({max.X},{max.Y},{max.Z}) with index {idx}");
    }
}
