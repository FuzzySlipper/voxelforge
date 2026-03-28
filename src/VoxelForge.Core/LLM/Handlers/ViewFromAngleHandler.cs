using System.Text.Json;
using VoxelForge.Core.Screenshot;

namespace VoxelForge.Core.LLM.Handlers;

/// <summary>
/// Captures a screenshot from a specific camera angle.
/// </summary>
public sealed class ViewFromAngleHandler : IToolHandler
{
    private readonly Func<IScreenshotProvider?> _providerFactory;

    public string ToolName => "view_from_angle";

    public ViewFromAngleHandler(Func<IScreenshotProvider?> providerFactory)
    {
        _providerFactory = providerFactory;
    }

    public ToolDefinition GetDefinition() => new()
    {
        Name = ToolName,
        Description = "Capture a screenshot from a specific camera angle. Yaw and pitch in radians. Returns base64 PNG.",
        ParametersSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "yaw": { "type": "number", "description": "Camera yaw in radians (0=front, pi/2=right, pi=back)" },
                "pitch": { "type": "number", "description": "Camera pitch in radians (0=level, pi/2=top-down)" }
            },
            "required": ["yaw", "pitch"]
        }
        """).RootElement,
    };

    public ToolHandlerResult Handle(JsonElement arguments, VoxelModel model, LabelIndex labels, List<AnimationClip> clips)
    {
        var provider = _providerFactory();
        if (provider is null)
            return new ToolHandlerResult { Content = "Screenshot provider not available.", IsError = true };

        float yaw = arguments.GetProperty("yaw").GetSingle();
        float pitch = arguments.GetProperty("pitch").GetSingle();

        var bytes = provider.CaptureFromAngle(yaw, pitch);
        var base64 = Convert.ToBase64String(bytes);

        return new ToolHandlerResult
        {
            Content = $"[image:png;base64]{base64}",
        };
    }
}
