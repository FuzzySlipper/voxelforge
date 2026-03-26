using System.Text.Json;

namespace VoxelForge.Core.LLM.Handlers;

public sealed class SetVoxelsHandler : IToolHandler
{
    public string ToolName => "set_voxels";

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
        int count = 0;

        // Capture for deferred apply
        var toSet = new List<(Point3 Pos, byte Index)>();
        foreach (var v in voxels.EnumerateArray())
        {
            int x = v.GetProperty("x").GetInt32();
            int y = v.GetProperty("y").GetInt32();
            int z = v.GetProperty("z").GetInt32();
            byte i = (byte)v.GetProperty("i").GetInt32();
            toSet.Add((new Point3(x, y, z), i));
            count++;
        }

        return new ToolHandlerResult
        {
            Content = $"Set {count} voxel(s).",
            ApplyAction = () =>
            {
                foreach (var (pos, idx) in toSet)
                    model.SetVoxel(pos, idx);
            },
        };
    }
}
