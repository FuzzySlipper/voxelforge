using Microsoft.Extensions.Logging;

namespace VoxelForge.App.Console;

/// <summary>
/// Dispatches command strings to IConsoleCommand implementations.
/// Shared by both the interactive console and stdio JSON-line protocol.
/// </summary>
public sealed class CommandRouter
{
    private readonly Dictionary<string, IConsoleCommand> _commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<CommandRouter> _logger;

    public IReadOnlyDictionary<string, IConsoleCommand> Commands => _commands;

    public CommandRouter(IEnumerable<IConsoleCommand> commands, ILogger<CommandRouter> logger)
    {
        _logger = logger;
        foreach (var cmd in commands)
        {
            _commands[cmd.Name] = cmd;
            foreach (var alias in cmd.Aliases)
                _commands[alias] = cmd;
        }
    }

    public CommandResult Execute(string input, CommandContext context)
    {
        var tokens = Tokenize(input);
        if (tokens.Length == 0)
            return CommandResult.Fail("Empty command.");

        var name = tokens[0];
        var args = tokens.Length > 1 ? tokens[1..] : [];

        if (!_commands.TryGetValue(name, out var command))
        {
            _logger.LogDebug("Unknown command: {Name}", name);
            return CommandResult.Fail($"Unknown command: '{name}'. Type 'help' for available commands.");
        }

        try
        {
            _logger.LogDebug("Executing: {Input}", input);
            var result = command.Execute(args, context);
            _logger.LogDebug("Result: {Success} — {Message}", result.Success,
                result.Message.Length > 200 ? result.Message[..200] + "..." : result.Message);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Command '{Name}' threw an exception", name);
            return CommandResult.Fail($"Error: {ex.Message}");
        }
    }

    private static string[] Tokenize(string input)
    {
        // Simple split on whitespace, respecting double-quoted strings
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        foreach (char c in input)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ' ' && !inQuotes)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
            tokens.Add(current.ToString());

        return tokens.ToArray();
    }
}
