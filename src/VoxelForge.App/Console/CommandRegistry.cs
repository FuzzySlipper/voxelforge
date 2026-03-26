using Microsoft.Extensions.Logging;
using VoxelForge.App.Console.Commands;

namespace VoxelForge.App.Console;

/// <summary>
/// Builds the full set of console commands. Explicit registration — no reflection.
/// </summary>
public static class CommandRegistry
{
    public static CommandRouter Build(ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger<CommandRouter>();

        // Create router first (HelpCommand needs it)
        CommandRouter? router = null;

        var commands = new List<IConsoleCommand>
        {
            new DescribeCommand(),
            new SetVoxelConsoleCommand(),
            new RemoveVoxelConsoleCommand(),
            new FillCommand(),
            new GetVoxelCommand(),
            new UndoCommand(),
            new RedoCommand(),
            new ListRegionsCommand(),
            new LabelVoxelCommand(),
            new PaletteCommand(),
            new SaveCommand(loggerFactory),
            new LoadCommand(loggerFactory),
            new ClearCommand(),
        };

        router = new CommandRouter(commands, logger);

        // Add help command (needs reference to router)
        var allCommands = new List<IConsoleCommand>(commands) { new HelpCommand(router) };
        router = new CommandRouter(allCommands, logger);

        return router;
    }
}
