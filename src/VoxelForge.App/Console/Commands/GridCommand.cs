namespace VoxelForge.App.Console.Commands;

public sealed class GridCommand : IConsoleCommand
{
    public string Name => "grid";
    public string[] Aliases => [];
    public string HelpText => "Get or set grid hint size. Usage: grid | grid <size>";

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length == 0)
            return CommandResult.Ok($"Grid hint: {context.Model.GridHint}");

        if (!int.TryParse(args[0], out int size) || size < 1 || size > 256)
            return CommandResult.Fail("Invalid size. Expected integer 1-256.");

        context.Model.GridHint = size;
        return CommandResult.Ok($"Grid hint set to {size}");
    }
}
