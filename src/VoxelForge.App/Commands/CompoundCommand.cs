namespace VoxelForge.App.Commands;

public sealed class CompoundCommand : IEditorCommand
{
    private readonly IReadOnlyList<IEditorCommand> _commands;

    public string Description { get; }

    public CompoundCommand(IReadOnlyList<IEditorCommand> commands, string description)
    {
        _commands = commands;
        Description = description;
    }

    public void Execute()
    {
        for (int i = 0; i < _commands.Count; i++)
            _commands[i].Execute();
    }

    public void Undo()
    {
        for (int i = _commands.Count - 1; i >= 0; i--)
            _commands[i].Undo();
    }
}
