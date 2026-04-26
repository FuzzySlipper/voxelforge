using VoxelForge.App.Services;
using VoxelForge.Core;

namespace VoxelForge.App.Console.Commands;

public sealed class FillCommand : IConsoleCommand
{
    private readonly VoxelEditingService _editingService;

    public string Name => "fill";
    public string[] Aliases => ["f"];
    public string HelpText => "Fill a region. Usage: fill <x1> <y1> <z1> <x2> <y2> <z2> <paletteIndex>";

    public FillCommand(VoxelEditingService editingService)
    {
        _editingService = editingService;
    }

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
        var result = _editingService.FillRegion(
            context.Document,
            context.UndoStack,
            context.Events,
            new FillVoxelRegionRequest(min, max, idx));

        return result.Success ? CommandResult.Ok(result.Message) : CommandResult.Fail(result.Message);
    }
}
