using System.Text.Json;
using VoxelForge.Core.Services;

namespace VoxelForge.Core.LLM.Handlers;

public sealed class DescribeModelHandler : IToolHandler
{
    private readonly VoxelQueryService _queryService;

    public string ToolName => "describe_model";

    public DescribeModelHandler(VoxelQueryService queryService)
    {
        _queryService = queryService;
    }

    public ToolDefinition GetDefinition() => new()
    {
        Name = ToolName,
        Description = "Get a text description of the voxel model for understanding its contents.",
        ParametersSchema = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement,
    };

    public ToolHandlerResult Handle(JsonElement arguments, VoxelModel model, LabelIndex labels, List<AnimationClip> clips)
    {
        return new ToolHandlerResult
        {
            Content = _queryService.DescribeModel(model, labels, clips),
        };
    }
}
