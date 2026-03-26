using Spectre.Console;

namespace VoxelForge.App.Console;

/// <summary>
/// Interactive REPL that runs in the terminal alongside the MonoGame window.
/// Uses Spectre.Console for input/output.
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

        while (!ct.IsCancellationRequested)
        {
            string input;
            try
            {
                input = AnsiConsole.Prompt(
                    new TextPrompt<string>("[grey]>[/]")
                        .AllowEmpty());
            }
            catch (OperationCanceledException)
            {
                break;
            }

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
}
