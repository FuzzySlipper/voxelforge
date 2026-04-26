using System.Text.Json;
using VoxelForge.Core.Services;

namespace VoxelForge.Core.LLM.Handlers;

public sealed class RemoveVoxelsHandler : IToolHandler
{
    private readonly VoxelMutationIntentService _intentService;

    public string ToolName => "remove_voxels";

    public RemoveVoxelsHandler(VoxelMutationIntentService intentService)
    {
        _intentService = intentService;
    }

    public ToolDefinition GetDefinition() => new()
    {
        Name = ToolName,
        Description = "Remove voxels at the specified coordinates.",
        ParametersSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "positions": {
                    "type": "array",
                    "items": {
                        "type": "object",
                        "properties": {
                            "x": { "type": "integer" },
                            "y": { "type": "integer" },
                            "z": { "type": "integer" }
                        },
                        "required": ["x", "y", "z"]
                    }
                }
            },
            "required": ["positions"]
        }
        """).RootElement,
    };

    public ToolHandlerResult Handle(JsonElement arguments, VoxelModel model, LabelIndex labels, List<AnimationClip> clips)
    {
        var positions = arguments.GetProperty("positions");
        var requests = new List<Point3>();
        foreach (var position in positions.EnumerateArray())
        {
            requests.Add(new Point3(
                position.GetProperty("x").GetInt32(),
                position.GetProperty("y").GetInt32(),
                position.GetProperty("z").GetInt32()));
        }

        var result = _intentService.BuildRemoveIntent(requests);
        return new ToolHandlerResult
        {
            Content = result.Message,
            IsError = !result.Success,
            MutationIntent = result.Intent,
        };
    }
}
