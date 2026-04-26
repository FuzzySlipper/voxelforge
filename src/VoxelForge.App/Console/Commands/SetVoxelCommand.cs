using VoxelForge.App.Services;
using VoxelForge.Core;

namespace VoxelForge.App.Console.Commands;

public sealed class SetVoxelConsoleCommand : IConsoleCommand
{
    private readonly VoxelEditingService _editingService;

    public string Name => "set";
    public string[] Aliases => ["s", "place"];
    public string HelpText => "Set a voxel. Usage: set <x> <y> <z> <paletteIndex>";

    public SetVoxelConsoleCommand(VoxelEditingService editingService)
    {
        _editingService = editingService;
    }

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 4)
            return CommandResult.Fail("Usage: set <x> <y> <z> <paletteIndex>");

        if (!int.TryParse(args[0], out int x) || !int.TryParse(args[1], out int y) ||
            !int.TryParse(args[2], out int z) || !byte.TryParse(args[3], out byte idx))
            return CommandResult.Fail("Invalid arguments. Expected integers for x,y,z and byte for index.");

        var result = _editingService.SetVoxel(
            context.Document,
            context.UndoStack,
            context.Events,
            new SetVoxelRequest(new Point3(x, y, z), idx));

        return result.Success ? CommandResult.Ok(result.Message) : CommandResult.Fail(result.Message);
    }
}
