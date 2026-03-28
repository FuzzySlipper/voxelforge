namespace VoxelForge.App.Console.Commands;

/// <summary>
/// Executes a script file containing one console command per line.
/// Blank lines and lines starting with # are skipped.
/// </summary>
public sealed class ExecCommand : IConsoleCommand
{
    private readonly CommandRouter _router;

    public string Name => "exec";
    public string[] Aliases => ["run"];
    public string HelpText => "Run a script file. Usage: exec <filepath>";

    public ExecCommand(CommandRouter router) => _router = router;

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 1)
            return CommandResult.Fail("Usage: exec <filepath>");

        var path = args[0];
        if (!File.Exists(path))
            return CommandResult.Fail($"Script not found: {path}");

        var lines = File.ReadAllLines(path);
        int executed = 0;
        int failed = 0;
        var errors = new List<string>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
                continue;

            var result = _router.Execute(line, context);
            executed++;

            if (!result.Success)
            {
                failed++;
                errors.Add($"  line {i + 1}: {result.Message}");
            }
        }

        if (failed > 0)
        {
            var errMsg = string.Join("\n", errors);
            return CommandResult.Ok($"Executed {executed} commands ({failed} failed):\n{errMsg}");
        }

        return CommandResult.Ok($"Executed {executed} commands.");
    }
}
