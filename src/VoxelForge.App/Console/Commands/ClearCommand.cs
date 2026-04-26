using VoxelForge.App.Services;

namespace VoxelForge.App.Console.Commands;

public sealed class ClearCommand : IConsoleCommand
{
    private readonly VoxelEditingService _editingService;

    public string Name => "clear";
    public string[] Aliases => ["cls"];
    public string HelpText => "Remove all voxels from the model.";

    public ClearCommand(VoxelEditingService editingService)
    {
        _editingService = editingService;
    }

    public CommandResult Execute(string[] args, CommandContext context)
    {
        var result = _editingService.Clear(context.Document, context.UndoStack, context.Events);
        return result.Success ? CommandResult.Ok(result.Message) : CommandResult.Fail(result.Message);
    }
}
