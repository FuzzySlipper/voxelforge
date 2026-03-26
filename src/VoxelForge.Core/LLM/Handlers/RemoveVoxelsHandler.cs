using System.Text.Json;

namespace VoxelForge.Core.LLM.Handlers;

public sealed class RemoveVoxelsHandler : IToolHandler
{
    public string ToolName => "remove_voxels";

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
        var toRemove = new List<Point3>();
        foreach (var p in positions.EnumerateArray())
            toRemove.Add(new Point3(p.GetProperty("x").GetInt32(), p.GetProperty("y").GetInt32(), p.GetProperty("z").GetInt32()));

        return new ToolHandlerResult
        {
            Content = $"Removed {toRemove.Count} voxel(s).",
            ApplyAction = () =>
            {
                foreach (var pos in toRemove)
                    model.RemoveVoxel(pos);
            },
        };
    }
}
