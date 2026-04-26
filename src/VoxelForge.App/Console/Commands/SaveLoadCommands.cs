using VoxelForge.App.Services;

namespace VoxelForge.App.Console.Commands;

public sealed class SaveCommand : IConsoleCommand
{
    private readonly ProjectLifecycleService _projectLifecycleService;

    public string Name => "save";
    public string[] Aliases => [];
    public string HelpText => "Save project. Usage: save <name> (saves to content/<name>.vforge)";

    public SaveCommand(ProjectLifecycleService projectLifecycleService)
    {
        _projectLifecycleService = projectLifecycleService;
    }

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 1)
            return CommandResult.Fail("Usage: save <name>");

        var result = _projectLifecycleService.Save(
            context.Document,
            context.Events,
            new SaveProjectRequest(args[0]));
        return result.Success ? CommandResult.Ok(result.Message) : CommandResult.Fail(result.Message);
    }

    internal static string ResolvePath(string input) => ProjectLifecycleService.ResolvePath(input);
}

public sealed class LoadCommand : IConsoleCommand
{
    private readonly ProjectLifecycleService _projectLifecycleService;

    public string Name => "load";
    public string[] Aliases => [];
    public string HelpText => "Load project. Usage: load <name> (loads from content/<name>.vforge)";

    public LoadCommand(ProjectLifecycleService projectLifecycleService)
    {
        _projectLifecycleService = projectLifecycleService;
    }

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 1)
            return CommandResult.Fail("Usage: load <name>");

        var result = _projectLifecycleService.Load(
            context.Document,
            context.UndoStack,
            context.Events,
            new LoadProjectRequest(args[0]));
        return result.Success ? CommandResult.Ok(result.Message) : CommandResult.Fail(result.Message);
    }
}
