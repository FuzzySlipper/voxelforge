namespace VoxelForge.App.Console.Commands;

public sealed class UndoCommand : IConsoleCommand
{
    public string Name => "undo";
    public string[] Aliases => ["u"];
    public string HelpText => "Undo the last operation.";

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (!context.UndoStack.CanUndo)
            return CommandResult.Fail("Nothing to undo.");

        context.UndoStack.Undo();
        context.OnModelChanged?.Invoke();
        return CommandResult.Ok("Undone.");
    }
}

public sealed class RedoCommand : IConsoleCommand
{
    public string Name => "redo";
    public string[] Aliases => ["r"];
    public string HelpText => "Redo the last undone operation.";

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (!context.UndoStack.CanRedo)
            return CommandResult.Fail("Nothing to redo.");

        context.UndoStack.Redo();
        context.OnModelChanged?.Invoke();
        return CommandResult.Ok("Redone.");
    }
}
