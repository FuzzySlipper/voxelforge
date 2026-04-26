using VoxelForge.App.Events;
using VoxelForge.Core;

namespace VoxelForge.App.Console.Commands;

public sealed class ListRegionsCommand : IConsoleCommand
{
    public string Name => "regions";
    public string[] Aliases => ["lr"];
    public string HelpText => "List all labeled regions.";

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (context.Labels.Regions.Count == 0)
            return CommandResult.Ok("No regions defined.");

        var lines = new List<string>();
        foreach (var (id, def) in context.Labels.Regions)
        {
            var parent = def.ParentId.HasValue ? $" (parent: {def.ParentId.Value})" : "";
            lines.Add($"  {def.Name}: {def.Voxels.Count} voxels{parent}");
        }

        return CommandResult.Ok(string.Join("\n", lines));
    }
}

public sealed class LabelVoxelCommand : IConsoleCommand
{
    public string Name => "label";
    public string[] Aliases => [];
    public string HelpText => "Label a voxel. Usage: label <regionName> <x> <y> <z>";

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 4)
            return CommandResult.Fail("Usage: label <regionName> <x> <y> <z>");

        var regionId = new RegionId(args[0]);
        bool createdRegion = false;
        if (!context.Labels.Regions.ContainsKey(regionId))
        {
            context.Labels.AddOrUpdateRegion(new RegionDef { Id = regionId, Name = args[0] });
            createdRegion = true;
        }

        if (!int.TryParse(args[1], out int x) || !int.TryParse(args[2], out int y) ||
            !int.TryParse(args[3], out int z))
            return CommandResult.Fail("Invalid coordinates.");

        var pos = new Point3(x, y, z);
        context.Labels.AssignRegion(regionId, [pos]);
        if (createdRegion)
        {
            context.Events.Publish(new LabelChangedEvent(
                LabelChangeKind.RegionCreated,
                $"Created region '{args[0]}'",
                regionId,
                0));
        }
        context.Events.Publish(new LabelChangedEvent(
            LabelChangeKind.RegionAssigned,
            $"Labeled ({x},{y},{z}) as '{args[0]}'",
            regionId,
            1));
        return CommandResult.Ok($"Labeled ({x},{y},{z}) as '{args[0]}'");
    }
}
