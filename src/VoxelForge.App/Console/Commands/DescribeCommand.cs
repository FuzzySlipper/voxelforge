using VoxelForge.Core.Services;

namespace VoxelForge.App.Console.Commands;

public sealed class DescribeCommand : IConsoleCommand
{
    private readonly VoxelQueryService _queryService;

    public string Name => "describe";
    public string[] Aliases => ["desc", "info"];
    public string HelpText => "Describe the current voxel model (palette, bounds, regions, materials).";

    public DescribeCommand(VoxelQueryService queryService)
    {
        _queryService = queryService;
    }

    public CommandResult Execute(string[] args, CommandContext context)
    {
        var description = _queryService.DescribeModel(context.Model, context.Labels, context.Clips);
        return CommandResult.Ok(description);
    }
}
