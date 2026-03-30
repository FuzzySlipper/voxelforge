using VoxelForge.App.Console;

namespace VoxelForge.Engine.MonoGame.UI;

/// <summary>
/// Bridges Myra menu item clicks to console command execution.
/// Ensures GUI, console, and LLM all share the same command path.
/// </summary>
public sealed class MenuCommandDispatcher
{
    private readonly CommandRouter _router;
    private readonly CommandContext _context;

    /// <summary>
    /// Fired after every command execution with the result.
    /// UI can subscribe to show feedback (status bar, toast, etc.)
    /// </summary>
    public event Action<CommandResult>? CommandExecuted;

    public MenuCommandDispatcher(CommandRouter router, CommandContext context)
    {
        _router = router;
        _context = context;
    }

    /// <summary>
    /// Execute a full command string (e.g. "refload golem.fbx") through the router.
    /// </summary>
    public CommandResult Dispatch(string commandString)
    {
        var result = _router.Execute(commandString, _context);
        CommandExecuted?.Invoke(result);
        return result;
    }

    /// <summary>
    /// Execute a command by name with separate args (e.g. "save", ["myproject"]).
    /// Args containing spaces are automatically quoted for the tokenizer.
    /// </summary>
    public CommandResult Dispatch(string commandName, params string[] args)
    {
        if (args.Length == 0)
            return Dispatch(commandName);

        var quoted = args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a);
        return Dispatch($"{commandName} {string.Join(" ", quoted)}");
    }

    public IReadOnlyDictionary<string, IConsoleCommand> Commands => _router.Commands;
}
