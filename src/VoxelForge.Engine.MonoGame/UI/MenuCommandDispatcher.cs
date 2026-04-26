using VoxelForge.App.Console;

namespace VoxelForge.Engine.MonoGame.UI;

/// <summary>
/// Bridges Myra menu item clicks to explicit console command adapters without
/// rebuilding command lines. Interactive console and stdio remain string/JSON
/// front doors; GUI code passes a command name plus typed argument values.
/// </summary>
public sealed class MenuCommandDispatcher
{
    private readonly CommandRouter _router;
    private readonly CommandContext _context;

    public MenuCommandDispatcher(CommandRouter router, CommandContext context)
    {
        _router = router;
        _context = context;
    }

    public CommandResult Dispatch(string commandName)
    {
        return _router.Execute(commandName, Array.Empty<string>(), _context);
    }

    public CommandResult Dispatch(string commandName, params string[] args)
    {
        return _router.Execute(commandName, args, _context);
    }

    public IReadOnlyDictionary<string, IConsoleCommand> Commands => _router.Commands;
}
