using VoxelForge.App.Services;
using VoxelForge.Core;

namespace VoxelForge.App.Console.Commands;

public sealed class ListRegionsCommand : IConsoleCommand
{
    private readonly RegionEditingService _regionEditingService;

    public string Name => "regions";
    public string[] Aliases => ["lr"];
    public string HelpText => "List all labeled regions.";

    public ListRegionsCommand(RegionEditingService regionEditingService)
    {
        _regionEditingService = regionEditingService;
    }

    public CommandResult Execute(string[] args, CommandContext context)
    {
        var result = _regionEditingService.ListRegions(context.Labels);
        if (result.Data is null || result.Data.Count == 0)
            return CommandResult.Ok(result.Message);

        var lines = new List<string>();
        for (int i = 0; i < result.Data.Count; i++)
        {
            var entry = result.Data[i];
            var parent = entry.ParentId.HasValue ? $" (parent: {entry.ParentId.Value})" : "";
            lines.Add($"  {entry.Name}: {entry.VoxelCount} voxels{parent}");
        }

        return CommandResult.Ok(string.Join("\n", lines));
    }
}

public sealed class LabelVoxelCommand : IConsoleCommand
{
    private readonly RegionEditingService _regionEditingService;

    public string Name => "label";
    public string[] Aliases => [];
    public string HelpText => "Label a voxel. Usage: label <regionName> <x> <y> <z>";

    public LabelVoxelCommand(RegionEditingService regionEditingService)
    {
        _regionEditingService = regionEditingService;
    }

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 4)
            return CommandResult.Fail("Usage: label <regionName> <x> <y> <z>");

        if (!int.TryParse(args[1], out int x) || !int.TryParse(args[2], out int y) ||
            !int.TryParse(args[3], out int z))
            return CommandResult.Fail("Invalid coordinates.");

        var result = _regionEditingService.AssignVoxel(
            context.Document,
            context.UndoStack,
            context.Events,
            new AssignVoxelRegionRequest(args[0], new Point3(x, y, z)));
        return result.Success ? CommandResult.Ok(result.Message) : CommandResult.Fail(result.Message);
    }
}
