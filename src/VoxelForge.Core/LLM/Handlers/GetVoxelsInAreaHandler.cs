using System.Text.Json;
using VoxelForge.Core.Services;

namespace VoxelForge.Core.LLM.Handlers;

public sealed class GetVoxelsInAreaHandler : IToolHandler
{
    private readonly VoxelQueryService _queryService;

    public string ToolName => "get_voxels_in_area";

    public GetVoxelsInAreaHandler(VoxelQueryService queryService)
    {
        _queryService = queryService;
    }

    public ToolDefinition GetDefinition() => new()
    {
        Name = ToolName,
        Description = "Query all voxels within a bounding box. Returns position and palette index for each.",
        ParametersSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "min_x": { "type": "integer" }, "min_y": { "type": "integer" }, "min_z": { "type": "integer" },
                "max_x": { "type": "integer" }, "max_y": { "type": "integer" }, "max_z": { "type": "integer" }
            },
            "required": ["min_x", "min_y", "min_z", "max_x", "max_y", "max_z"]
        }
        """).RootElement,
    };

    public ToolHandlerResult Handle(JsonElement arguments, VoxelModel model, LabelIndex labels, List<AnimationClip> clips)
    {
        int minX = arguments.GetProperty("min_x").GetInt32();
        int minY = arguments.GetProperty("min_y").GetInt32();
        int minZ = arguments.GetProperty("min_z").GetInt32();
        int maxX = arguments.GetProperty("max_x").GetInt32();
        int maxY = arguments.GetProperty("max_y").GetInt32();
        int maxZ = arguments.GetProperty("max_z").GetInt32();

        var request = new VoxelBoxQueryRequest(
            new Point3(minX, minY, minZ),
            new Point3(maxX, maxY, maxZ));
        var result = _queryService.GetVoxelsInArea(model, request);

        return new ToolHandlerResult
        {
            Content = JsonSerializer.Serialize(result),
        };
    }
}
