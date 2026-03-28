using Spectre.Console;

namespace VoxelForge.App.Console;

/// <summary>
/// Interactive REPL that runs in the terminal alongside the MonoGame window.
/// Uses ReadLine for history and tab completion, Spectre.Console for output.
/// </summary>
public sealed class InteractiveConsoleHost
{
    private readonly CommandRouter _router;
    private readonly CommandContext _context;

    public InteractiveConsoleHost(CommandRouter router, CommandContext context)
    {
        _router = router;
        _context = context;
    }

    /// <summary>
    /// Run the REPL. Blocks the calling thread. Call from a background thread.
    /// </summary>
    public void Run(CancellationToken ct)
    {
        AnsiConsole.MarkupLine("[bold blue]VoxelForge Console[/] — type [green]help[/] for commands, [green]quit[/] to exit");

        // Set up tab completion with command names
        var commandNames = _router.Commands.Keys.Order().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        ReadLine.AutoCompletionHandler = new CommandAutoComplete(commandNames);
        ReadLine.HistoryEnabled = true;

        while (!ct.IsCancellationRequested)
        {
            string? input;
            try
            {
                input = ReadLine.Read("> ");
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (input is null) // EOF
                break;

            if (string.IsNullOrWhiteSpace(input))
                continue;

            if (input.Equals("quit", StringComparison.OrdinalIgnoreCase) ||
                input.Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            var result = _router.Execute(input, _context);

            if (result.Success)
                AnsiConsole.MarkupLine($"[green]{EscapeMarkup(result.Message)}[/]");
            else
                AnsiConsole.MarkupLine($"[red]{EscapeMarkup(result.Message)}[/]");
        }
    }

    private static string EscapeMarkup(string text) =>
        text.Replace("[", "[[").Replace("]", "]]");

    private sealed class CommandAutoComplete : IAutoCompleteHandler
    {
        private readonly List<string> _commandNames;

        public CommandAutoComplete(List<string> commandNames) => _commandNames = commandNames;

        public char[] Separators { get; set; } = [' '];

        public string[] GetSuggestions(string text, int index)
        {
            // Only autocomplete the first token (command name)
            var prefix = text.Split(' ')[0];
            return _commandNames
                .Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
    }
}
