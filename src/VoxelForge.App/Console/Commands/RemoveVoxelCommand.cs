using VoxelForge.App.Services;
using VoxelForge.Core;

namespace VoxelForge.App.Console.Commands;

public sealed class RemoveVoxelConsoleCommand : IConsoleCommand
{
    private readonly VoxelEditingService _editingService;

    public string Name => "remove";
    public string[] Aliases => ["rm", "delete"];
    public string HelpText => "Remove a voxel. Usage: remove <x> <y> <z>";

    public RemoveVoxelConsoleCommand(VoxelEditingService editingService)
    {
        _editingService = editingService;
    }

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 3)
            return CommandResult.Fail("Usage: remove <x> <y> <z>");

        if (!int.TryParse(args[0], out int x) || !int.TryParse(args[1], out int y) ||
            !int.TryParse(args[2], out int z))
            return CommandResult.Fail("Invalid arguments. Expected integers for x,y,z.");

        var result = _editingService.RemoveVoxel(
            context.Document,
            context.UndoStack,
            context.Events,
            new RemoveVoxelRequest(new Point3(x, y, z)));

        return result.Success ? CommandResult.Ok(result.Message) : CommandResult.Fail(result.Message);
    }
}
