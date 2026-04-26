using System.Text.Json;
using VoxelForge.Core.Services;

namespace VoxelForge.Core.LLM.Handlers;

public sealed class GetModelInfoHandler : IToolHandler
{
    private readonly VoxelQueryService _queryService;

    public string ToolName => "get_model_info";

    public GetModelInfoHandler(VoxelQueryService queryService)
    {
        _queryService = queryService;
    }

    public ToolDefinition GetDefinition() => new()
    {
        Name = ToolName,
        Description = "Get information about the current voxel model: dimensions, voxel count, palette, and region list.",
        ParametersSchema = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement,
    };

    public ToolHandlerResult Handle(JsonElement arguments, VoxelModel model, LabelIndex labels, List<AnimationClip> clips)
    {
        var info = _queryService.GetModelInfo(model, labels, clips);
        return new ToolHandlerResult { Content = JsonSerializer.Serialize(info) };
    }
}
