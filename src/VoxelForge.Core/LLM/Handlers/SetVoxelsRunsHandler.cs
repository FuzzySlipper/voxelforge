using System.Text.Json;
using VoxelForge.Core.Services;

namespace VoxelForge.Core.LLM.Handlers;

public sealed class SetVoxelsRunsHandler : IToolHandler
{
    private const int MaxTotalVoxels = 65536;
    private readonly VoxelMutationIntentService _intentService;

    public string ToolName => "set_voxels_runs";

    public SetVoxelsRunsHandler(VoxelMutationIntentService intentService)
    {
        _intentService = intentService;
    }

    public ToolDefinition GetDefinition() => new()
    {
        Name = ToolName,
        Description = "Set voxels using compact horizontal runs along the X axis.",
        ParametersSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "runs": {
                    "type": "array",
                    "items": {
                        "type": "object",
                        "properties": {
                            "x1": { "type": "integer", "description": "Start X coordinate (inclusive)" },
                            "x2": { "type": "integer", "description": "End X coordinate (inclusive)" },
                            "y": { "type": "integer" },
                            "z": { "type": "integer" },
                            "i": { "type": "integer", "description": "Palette index (1-255)" }
                        },
                        "required": ["x1", "x2", "y", "z", "i"]
                    }
                }
            },
            "required": ["runs"]
        }
        """).RootElement,
    };

    public ToolHandlerResult Handle(JsonElement arguments, VoxelModel model, LabelIndex labels, List<AnimationClip> clips)
    {
        var runs = arguments.GetProperty("runs");
        var requests = new List<VoxelAssignmentRequest>();
        int totalVoxelsFromRuns = 0;

        foreach (var run in runs.EnumerateArray())
        {
            int x1 = run.GetProperty("x1").GetInt32();
            int x2 = run.GetProperty("x2").GetInt32();
            int y = run.GetProperty("y").GetInt32();
            int z = run.GetProperty("z").GetInt32();
            int paletteIndex = run.GetProperty("i").GetInt32();

            if (x1 > x2)
            {
                return new ToolHandlerResult
                {
                    Content = $"Invalid run: x1 ({x1}) > x2 ({x2}). x1 must be less than or equal to x2.",
                    IsError = true,
                };
            }

            if (paletteIndex < 1 || paletteIndex > 255)
            {
                return new ToolHandlerResult
                {
                    Content = $"Invalid palette index {paletteIndex} in run x1={x1},x2={x2},y={y},z={z}. Expected 1-255.",
                    IsError = true,
                };
            }

            int runLength = x2 - x1 + 1;
            if (totalVoxelsFromRuns > MaxTotalVoxels - runLength)
            {
                return new ToolHandlerResult
                {
                    Content = $"Safety cap exceeded: attempting to set {totalVoxelsFromRuns + runLength} voxels, maximum is {MaxTotalVoxels}.",
                    IsError = true,
                };
            }

            totalVoxelsFromRuns += runLength;

            for (int x = x1; x <= x2; x++)
            {
                requests.Add(new VoxelAssignmentRequest(new Point3(x, y, z), paletteIndex));
            }
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
