using VoxelForge.App.Services;

namespace VoxelForge.App.Console.Commands;

public sealed class GridCommand : IConsoleCommand
{
    private readonly VoxelEditingService _editingService;

    public string Name => "grid";
    public string[] Aliases => [];
    public string HelpText => "Get or set grid hint size. Usage: grid | grid <size>";

    public GridCommand(VoxelEditingService editingService)
    {
        _editingService = editingService;
    }

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length == 0)
            return CommandResult.Ok($"Grid hint: {context.Model.GridHint}");

        if (!int.TryParse(args[0], out int size))
            return CommandResult.Fail("Invalid size. Expected integer 1-256.");

        var result = _editingService.SetGridHint(
            context.Document,
            context.UndoStack,
            context.Events,
            new SetGridHintRequest(size));

        return result.Success ? CommandResult.Ok(result.Message) : CommandResult.Fail(result.Message);
    }
}
