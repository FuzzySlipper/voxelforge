using VoxelForge.Core.LLM.Handlers;

namespace VoxelForge.App.Console.Commands;

public sealed class DescribeCommand : IConsoleCommand
{
    public string Name => "describe";
    public string[] Aliases => ["desc", "info"];
    public string HelpText => "Describe the current voxel model (palette, bounds, regions, materials).";

    public CommandResult Execute(string[] args, CommandContext context)
    {
        var handler = new DescribeModelHandler();
        var result = handler.Handle(
            System.Text.Json.JsonDocument.Parse("{}").RootElement,
            context.Model, context.Labels, context.Clips);
        return CommandResult.Ok(result.Content);
    }
}
