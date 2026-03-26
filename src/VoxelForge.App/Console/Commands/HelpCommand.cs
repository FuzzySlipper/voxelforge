namespace VoxelForge.App.Console.Commands;

public sealed class HelpCommand : IConsoleCommand
{
    private readonly CommandRouter _router;

    public string Name => "help";
    public string[] Aliases => ["?", "commands"];
    public string HelpText => "List available commands.";

    public HelpCommand(CommandRouter router) => _router = router;

    public CommandResult Execute(string[] args, CommandContext context)
    {
        var seen = new HashSet<string>();
        var lines = new List<string>();

        foreach (var (name, cmd) in _router.Commands.OrderBy(kv => kv.Key))
        {
            if (!seen.Add(cmd.Name)) continue;
            var aliases = cmd.Aliases.Length > 0 ? $" ({string.Join(", ", cmd.Aliases)})" : "";
            lines.Add($"  {cmd.Name,-20}{aliases,-15} {cmd.HelpText}");
        }

        return CommandResult.Ok(string.Join("\n", lines));
    }
}
