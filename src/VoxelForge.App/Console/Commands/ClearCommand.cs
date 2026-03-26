namespace VoxelForge.App.Console.Commands;

public sealed class ClearCommand : IConsoleCommand
{
    public string Name => "clear";
    public string[] Aliases => ["cls"];
    public string HelpText => "Remove all voxels from the model.";

    public CommandResult Execute(string[] args, CommandContext context)
    {
        var positions = context.Model.Voxels.Keys.ToList();
        foreach (var pos in positions)
            context.Model.RemoveVoxel(pos);

        context.OnModelChanged?.Invoke();
        return CommandResult.Ok($"Cleared {positions.Count} voxels.");
    }
}
