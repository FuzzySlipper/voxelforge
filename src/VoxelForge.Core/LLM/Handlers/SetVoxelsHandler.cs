using System.Text.Json;
using VoxelForge.Core.Services;

namespace VoxelForge.Core.LLM.Handlers;

public sealed class SetVoxelsHandler : IToolHandler
{
    private readonly VoxelMutationIntentService _intentService;

    public string ToolName => "set_voxels";

    public SetVoxelsHandler(VoxelMutationIntentService intentService)
    {
        _intentService = intentService;
    }

    public ToolDefinition GetDefinition() => new()
    {
        Name = ToolName,
        Description = "Set one or more voxels by coordinate and palette index.",
        ParametersSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "voxels": {
                    "type": "array",
                    "items": {
                        "type": "object",
                        "properties": {
                            "x": { "type": "integer" },
                            "y": { "type": "integer" },
                            "z": { "type": "integer" },
                            "i": { "type": "integer", "description": "Palette index (1-255)" }
                        },
                        "required": ["x", "y", "z", "i"]
                    }
                }
            },
            "required": ["voxels"]
        }
        """).RootElement,
    };

    public ToolHandlerResult Handle(JsonElement arguments, VoxelModel model, LabelIndex labels, List<AnimationClip> clips)
    {
        var voxels = arguments.GetProperty("voxels");
        var requests = new List<VoxelAssignmentRequest>();

        foreach (var voxel in voxels.EnumerateArray())
        {
            int x = voxel.GetProperty("x").GetInt32();
            int y = voxel.GetProperty("y").GetInt32();
            int z = voxel.GetProperty("z").GetInt32();
            int paletteIndex = voxel.GetProperty("i").GetInt32();
            requests.Add(new VoxelAssignmentRequest(new Point3(x, y, z), paletteIndex));
        }

        var result = _intentService.BuildSetIntent(requests);
        return new ToolHandlerResult
        {
            Content = result.Message,
            IsError = !result.Success,
            MutationIntent = result.Intent,
        };
    }
}
